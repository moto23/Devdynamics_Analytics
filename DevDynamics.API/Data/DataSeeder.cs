using DevDynamics.API.Models;

namespace DevDynamics.API.Data;

/// <summary>
/// Synthetic seed data. This is a temporary stand-in that will be replaced by
/// real GitHub-ingested data; it exists so the dashboard and the query
/// benchmark have a dataset to work against.
/// </summary>
public static class DataSeeder
{
    public static void Seed(AppDbContext context, int rowCount = 100)
    {
        if (rowCount <= 0) return;

        var existing = context.DevActivities.Count();
        if (existing >= rowCount) return;

        // Fixed seed: benchmark runs must be reproducible across restarts.
        var random = new Random(20260721);

        var contributors = new[] { "Alice", "Bob", "Carol", "David", "Emma" };
        var companies = new[] { "Google", "Microsoft", "Amazon", "Netflix" };

        var end = DateTime.UtcNow;
        var start = end.AddDays(-365);
        var totalSeconds = (end - start).TotalSeconds;

        var toCreate = rowCount - existing;
        var activities = new List<DevActivity>(toCreate);

        for (var i = 0; i < toCreate; i++)
        {
            var time = start.AddSeconds(random.NextDouble() * totalSeconds);

            activities.Add(new DevActivity
            {
                Time = time,
                Commits = random.Next(1, 10),
                PullRequests = random.Next(0, 5),
                Merges = random.Next(0, 5),
                Meetings = random.Next(0, 4),
                Documentation = random.Next(0, 6),
                Contributor = contributors[random.Next(contributors.Length)],
                Company = companies[random.Next(companies.Length)],
                PROpenedAt = time,
                PRMergedAt = time.AddHours(random.Next(2, 12))
            });
        }

        context.DevActivities.AddRange(activities);
        context.SaveChanges();
    }
}
