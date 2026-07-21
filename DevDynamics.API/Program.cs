using DevDynamics.API.Analytics;
using DevDynamics.API.Data;
using DevDynamics.API.GraphQL;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =========================
// Services
// =========================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// =========================
// Database provider
// =========================
// Production runs on Azure SQL (SQL Server). The connection string arrives via
// the ConnectionStrings__Default environment variable and is never committed.
//
// SQLite remains as a temporary fallback purely so the service keeps serving
// while the SQL Server connection string is being configured. It is scheduled
// for removal once GitHub ingestion lands.
var connectionString = builder.Configuration.GetConnectionString("Default");
var providerKind = DatabaseProvider.Detect(connectionString);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (providerKind == DatabaseProviderKind.SqlServer)
    {
        options.UseSqlServer(connectionString, sql =>
        {
            // Azure SQL serverless auto-pauses when idle; resuming it surfaces as
            // a transient fault. Retry so the first request after an idle period
            // waits for the resume instead of failing.
            sql.EnableRetryOnFailure(
                maxRetryCount: 6,
                maxRetryDelay: TimeSpan.FromSeconds(20),
                errorNumbersToAdd: null);

            sql.CommandTimeout(60);
        });
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// Resolvers need to know the provider to pick a translatable aggregation.
builder.Services.AddSingleton(new DatabaseProviderInfo(providerKind));

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .ModifyRequestOptions(o =>
        o.IncludeExceptionDetails = builder.Environment.IsDevelopment());

// CORS scoped to the known frontends instead of AllowAnyOrigin.
// Falls back to the deployed origins in code so a missing or malformed config
// section can never leave the production frontend unable to reach the API.
string[] defaultOrigins =
[
    "https://devdynamics-frontend.onrender.com",
    "http://localhost:4200"
];

var configuredOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

var allowedOrigins = configuredOrigins is { Length: > 0 }
    ? configuredOrigins
    : defaultOrigins;

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// =========================
// Middleware
// =========================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");

// NOTE: deliberately no UseHttpsRedirection(). Render terminates TLS at its
// proxy and forwards plain HTTP to the container, so redirecting here would
// produce a redirect loop.

// GraphQL endpoint
app.MapGraphQL("/graphql");

// Liveness probe (also useful for warming the free-tier cold start).
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow
}));

// Reports which database is actually serving traffic, and proves it by
// opening a connection. Deliberately exposes no credentials: host and database
// name only, and any error text is scrubbed of user id / password.
app.MapGet("/db-info", async (AppDbContext db) =>
{
    var connection = db.Database.GetDbConnection();

    var info = new Dictionary<string, object?>
    {
        ["provider"] = providerKind.ToString(),
        ["dataSource"] = connection.DataSource,
        ["database"] = connection.Database
    };

    try
    {
        // Cap the diagnostic so a blocked route reports in seconds rather than
        // hanging for the full connection timeout multiplied by the retry count.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        info["canConnect"] = await db.Database.CanConnectAsync(cts.Token);
        info["appliedMigrations"] = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        info["pendingMigrations"] = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        info["devActivityRows"] = await db.DevActivities.CountAsync();
    }
    catch (Exception ex)
    {
        info["canConnect"] = false;
        info["errorType"] = ex.GetType().Name;
        info["error"] = Scrub(ex.Message);
        info["innerError"] = ex.InnerException is null ? null : Scrub(ex.InnerException.Message);
    }

    return Results.Ok(info);
});

// Network reachability diagnostic. Answers the two questions that a hanging
// database connection cannot: what public IP does this service egress from
// (that is the address the SQL firewall must allow), and can it open a TCP
// socket to the database host at all. Fails fast rather than retrying, so a
// blocked route is reported in seconds instead of minutes.
app.MapGet("/net-info", async (AppDbContext db) =>
{
    var result = new Dictionary<string, object?>();

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    try
    {
        result["egressIp"] = (await http.GetStringAsync("https://api.ipify.org")).Trim();
    }
    catch (Exception ex)
    {
        result["egressIp"] = $"lookup failed: {ex.GetType().Name}";
    }

    // Parse host and port out of the configured data source, e.g.
    // "tcp:server.database.windows.net,1433".
    var dataSource = db.Database.GetDbConnection().DataSource ?? string.Empty;
    var hostPart = dataSource.Replace("tcp:", string.Empty, StringComparison.OrdinalIgnoreCase);
    var pieces = hostPart.Split(',');
    var host = pieces[0];
    var port = pieces.Length > 1 && int.TryParse(pieces[1], out var p) ? p : 1433;

    result["sqlHost"] = host;
    result["sqlPort"] = port;

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        var connectTask = tcp.ConnectAsync(host, port);
        var completed = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(10)));

        stopwatch.Stop();

        if (completed == connectTask && tcp.Connected)
        {
            result["tcpReachable"] = true;
            result["tcpNote"] = "TCP handshake succeeded; the SQL firewall permits this egress IP.";
        }
        else
        {
            result["tcpReachable"] = false;
            result["tcpNote"] = "Timed out with no response. Typical of a firewall silently dropping packets: "
                                + "add the egressIp above to the Azure SQL firewall.";
        }

        result["tcpElapsedMs"] = stopwatch.ElapsedMilliseconds;
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        result["tcpReachable"] = false;
        result["tcpError"] = ex.GetType().Name;
        result["tcpElapsedMs"] = stopwatch.ElapsedMilliseconds;
    }

    return Results.Ok(result);
});

// Strips anything credential-shaped out of diagnostic text.
static string Scrub(string message) => System.Text.RegularExpressions.Regex.Replace(
    message,
    @"(?i)(password|pwd|user\s*id|uid)\s*=\s*[^;]*",
    "$1=***");

// Before/after query benchmark. Read-only: it runs SELECTs only.
app.MapGet("/benchmark", async (AppDbContext db, int? iterations) =>
{
    var report = await new QueryBenchmark(db, providerKind)
        .RunAsync(iterations ?? 20);

    return Results.Ok(report);
});

app.MapGet("/", () => "API is running 🚀");

// =========================
// DB Init + Seed
// =========================
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    logger.LogInformation("Database provider: {Provider}", providerKind);

    try
    {
        if (providerKind == DatabaseProviderKind.SqlServer)
        {
            // Migrations are authored for SQL Server, which is the real target.
            dbContext.Database.Migrate();
        }
        else
        {
            // The SQLite fallback has no migration history of its own; build the
            // schema straight from the model. Temporary, see the note above.
            dbContext.Database.EnsureCreated();
        }

        if (builder.Configuration.GetValue("Seed:Enabled", true))
        {
            DataSeeder.Seed(dbContext, builder.Configuration.GetValue("Seed:RowCount", 100));
        }

        logger.LogInformation("Database ready.");
    }
    catch (Exception ex)
    {
        // Never let a database problem stop the process: the API should still
        // start and report errors per-request rather than crash-looping on Render.
        logger.LogError(ex, "Database initialisation failed. API is starting anyway.");
    }
}

// =========================
// Render Port Binding
// =========================
var port = Environment.GetEnvironmentVariable("PORT") ?? "5236";
app.Run($"http://0.0.0.0:{port}");
