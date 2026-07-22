using DevDynamics.API.Models;

namespace DevDynamics.API.Data;

/// <summary>
/// Seeds an initial set of repositories so a fresh deployment has something to
/// show.
///
/// These are ordinary rows, flagged IsDemoData purely for display. They can be
/// removed or replaced through the management API like any other repository,
/// and nothing in the platform's logic references them. The list is
/// configuration (Registry:DemoRepositories), so changing it needs no code
/// change, and seeding runs only when the registry is completely empty — it
/// never resurrects repositories a user has deleted.
/// </summary>
public static class RegistrySeeder
{
    /// <summary>
    /// Fallback used only when no configuration is supplied. Chosen as the
    /// open-source projects this application itself is built on.
    /// </summary>
    private static readonly string[] DefaultDemoRepositories =
    [
        "chartjs/Chart.js",
        "ChilliCream/graphql-platform",
        "dotnet/efcore",
        "angular/components",
        "supabase/supabase",
        "moto23/Devdynamics_Analytics"
    ];

    public static int Seed(AppDbContext context, IConfiguration configuration, ILogger logger)
    {
        // Only ever seed an empty registry.
        if (context.TrackedRepositories.Any())
        {
            return 0;
        }

        var configured = configuration
            .GetSection("Registry:DemoRepositories")
            .Get<string[]>();

        var repositories = configured is { Length: > 0 }
            ? configured
            : DefaultDemoRepositories;

        var seeded = repositories
            .Where(fullName => !string.IsNullOrWhiteSpace(fullName) && fullName.Contains('/'))
            .Select(fullName =>
            {
                var parts = fullName.Split('/', 2);

                return new TrackedRepository
                {
                    Provider = "GitHub",
                    // Filled in by the first sync, which resolves real metadata.
                    ExternalId = $"pending:{fullName}",
                    Owner = parts[0],
                    Name = parts[1],
                    FullName = fullName,
                    IsActive = true,
                    IsDemoData = true,
                    AddedAtUtc = DateTime.UtcNow,
                    AddedBy = "demo-seed",
                    SyncStatus = SyncStatuses.NeverSynced
                };
            })
            .ToList();

        context.TrackedRepositories.AddRange(seeded);
        context.SaveChanges();

        logger.LogInformation(
            "Seeded {Count} demo repositories into an empty registry.", seeded.Count);

        return seeded.Count;
    }
}
