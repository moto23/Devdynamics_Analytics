using DevDynamics.API.Data;
using DevDynamics.API.GraphQL;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =========================
// Services
// =========================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

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

app.MapGet("/", () => "API is running 🚀");

// =========================
// DB Init + Seed
// =========================
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();

    // Synthetic seed data is a temporary stand-in until GitHub ingestion lands.
    if (builder.Configuration.GetValue("Seed:Enabled", true))
    {
        DataSeeder.Seed(dbContext);
    }
}

// =========================
// Render Port Binding
// =========================
var port = Environment.GetEnvironmentVariable("PORT") ?? "5236";
app.Run($"http://0.0.0.0:{port}");
