using DevDynamics.API.Data;
using DevDynamics.API.GraphQL;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// =========================
// Services
// =========================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>();

// 🔥 Updated CORS (for deployment)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .AllowAnyOrigin() // change later to your Vercel domain
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

// ⚠️ Optional: you can remove HTTPS redirection on Render if issues come
app.UseHttpsRedirection();

app.MapControllers();
app.MapGraphQL();

// =========================
// DB Init + Seed
// =========================
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
    DataSeeder.Seed(dbContext);
}

// =========================
// 🔥 Render Port Binding
// =========================
var port = Environment.GetEnvironmentVariable("5236") ?? "5000";
app.Run($"http://0.0.0.0:{5236}");