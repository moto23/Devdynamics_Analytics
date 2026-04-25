using DevDynamics.API.Models;

namespace DevDynamics.API.Data;

public static class DataSeeder
{
    public static void Seed(AppDbContext context)
    {
        if (context.DevActivities.Any()) return;

        var random = new Random();

var contributors = new[] { "Alice", "Bob", "Carol", "David", "Emma" };
var companies = new[] { "Google", "Microsoft", "Amazon", "Netflix" };

var end = DateTime.UtcNow;
var start = end.AddDays(-30);

var activities = new List<DevActivity>();

for (var i = 0; i < 100; i++)
{
    var time = start.AddSeconds(random.NextDouble() * (end - start).TotalSeconds);

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