using DevDynamics.API.Analytics;
using DevDynamics.API.Data;
using DevDynamics.API.GraphQL;
using DevDynamics.API.SourceControl;
using DevDynamics.API.SourceControl.GitHub;
using DevDynamics.API.Sync;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =========================
// Database provider
// =========================
// Production runs on Azure SQL (SQL Server). The connection string arrives via
// the ConnectionStrings__Default environment variable and is never committed.
//
// SQLite remains as a fallback so the service keeps serving if no SQL Server
// connection string is configured.
var connectionString = builder.Configuration.GetConnectionString("Default");
var providerKind = DatabaseProvider.Detect(connectionString);

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

var databaseOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();

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
                maxRetryCount: databaseOptions.MaxRetryCount,
                maxRetryDelay: TimeSpan.FromSeconds(databaseOptions.MaxRetryDelaySeconds),
                errorNumbersToAdd: null);

            sql.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
        });
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// Resolvers need to know the provider to pick a translatable aggregation.
builder.Services.AddSingleton(new DatabaseProviderInfo(providerKind));

// =========================
// Source control ingestion
// =========================
builder.Services.Configure<GitHubOptions>(
    builder.Configuration.GetSection(GitHubOptions.SectionName));

// One provider today. Registering another Git host is a single extra line here
// plus its ISourceControlProvider implementation; nothing else changes.
builder.Services.AddHttpClient<GitHubProvider>();
builder.Services.AddScoped<ISourceControlProvider>(sp => sp.GetRequiredService<GitHubProvider>());
builder.Services.AddScoped<ISourceControlProviderRegistry, SourceControlProviderRegistry>();

builder.Services.AddScoped<RepositorySyncService>();
builder.Services.AddScoped<RepositoryRegistryService>();

builder.Services.AddSingleton<ISyncQueue, SyncQueue>();
builder.Services.AddHostedService<SyncWorker>();

// Optional scheduled refresh; disabled unless Schedule:Enabled is set.
builder.Services.Configure<ScheduleOptions>(
    builder.Configuration.GetSection(ScheduleOptions.SectionName));
builder.Services.AddHostedService<ScheduledSyncWorker>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAdminAuthorizer, AdminAuthorizer>();

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
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
// No Swagger: this service exposes a GraphQL API plus a handful of operational
// endpoints, and GraphQL is self-describing through introspection.

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

// Strips anything credential-shaped out of diagnostic text.
static string Scrub(string message) => System.Text.RegularExpressions.Regex.Replace(
    message,
    @"(?i)(password|pwd|user\s*id|uid)\s*=\s*[^;]*",
    "$1=***");

// Before/after query benchmark. Read-only, but it runs many queries, so it is
// gated behind the admin key rather than left open.
app.MapGet("/benchmark", async (HttpContext http, AppDbContext db, IConfiguration config, int? iterations) =>
{
    var configured = config["Admin:ApiKey"];

    if (string.IsNullOrWhiteSpace(configured))
    {
        return Results.Problem("Benchmark is disabled: no admin key is configured.", statusCode: 503);
    }

    var supplied = http.Request.Headers["X-Admin-Key"].ToString();

    if (string.IsNullOrWhiteSpace(supplied) || supplied != configured)
    {
        return Results.Unauthorized();
    }

    var report = await new QueryBenchmark(db, providerKind).RunAsync(iterations ?? 20);
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
            // Azure SQL serverless auto-pauses when idle, and a resume can take
            // longer than the DbContext retry budget. Waking it explicitly before
            // migrating avoids starting up with no schema applied, which would
            // otherwise leave every query failing until the next restart.
            await WaitForDatabaseAsync(dbContext, logger, databaseOptions);

            // Migrations are authored for SQL Server, which is the real target.
            dbContext.Database.Migrate();
        }
        else
        {
            // The SQLite fallback has no migration history of its own; build the
            // schema straight from the model. Temporary, see the note above.
            dbContext.Database.EnsureCreated();
        }

        // Synthetic activity seeding is retired: analytics now read ingested
        // source-control data. Kept behind a default-off flag only so the
        // legacy query benchmark can still be reproduced on demand.
        if (builder.Configuration.GetValue("Seed:Enabled", false))
        {
            DataSeeder.Seed(dbContext, builder.Configuration.GetValue("Seed:RowCount", 100));
        }

        // Populates the repository registry only when it is completely empty.
        // These are ordinary, removable rows.
        RegistrySeeder.Seed(dbContext, builder.Configuration, logger);

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

// Polls until the database accepts a connection. Azure SQL serverless reports
// error 40613 ("not currently available") while resuming from auto-pause, which
// can outlast the DbContext's own retry budget.
static async Task WaitForDatabaseAsync(
    AppDbContext dbContext,
    ILogger logger,
    DatabaseOptions options)
{
    var maxAttempts = options.StartupWaitAttempts;

    if (maxAttempts <= 0)
    {
        return;
    }

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            if (await dbContext.Database.CanConnectAsync())
            {
                if (attempt > 1)
                {
                    logger.LogInformation("Database available after {Attempts} attempts.", attempt);
                }

                return;
            }
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogInformation(
                "Waiting for database to resume (attempt {Attempt}/{Max}): {Message}",
                attempt, maxAttempts, ex.Message);
        }

        await Task.Delay(TimeSpan.FromSeconds(options.StartupWaitIntervalSeconds));
    }

    logger.LogWarning("Database did not become available within the startup window.");
}
