using DevDynamics.API.Data;
using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.GraphQL;

public class Query
{
    public async Task<List<DevActivity>> GetDevActivities(
        [Service] AppDbContext context,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? company = null
    )
    {
        var query = context.DevActivities.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(x => x.Time >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(x => x.Time <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(contributor))
        {
            var c = contributor.Trim().ToLower();
            query = query.Where(x => x.Contributor.ToLower() == c);
        }

        if (!string.IsNullOrWhiteSpace(company))
        {
            var comp = company.Trim().ToLower();
            query = query.Where(x => x.Company.ToLower() == comp);
        }

        return await query
            .OrderBy(x => x.Time)
            .ToListAsync();
    }

    public async Task<List<PRCycleTimeResult>> GetPRCycleTime(
        [Service] AppDbContext context,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? company = null
    )
    {
        var query = context.DevActivities.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(x => x.Time >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(x => x.Time <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(company))
        {
            var comp = company.Trim().ToLower();
            query = query.Where(x => x.Company.ToLower() == comp);
        }

        var data = await query.ToListAsync();

        return data
            .GroupBy(x => x.Time.Date)
            .OrderBy(x => x.Key)
            .Select(g => new PRCycleTimeResult
            {
                Date = g.Key,
                AvgHours = g.Average(a => (a.PRMergedAt - a.PROpenedAt).TotalHours)
            })
            .ToList();
    }

    public async Task<SummaryResult> GetSummaryStats(
        [Service] AppDbContext context,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? company = null
    )
    {
        var query = context.DevActivities.AsQueryable();

        if (startDate.HasValue)
            query = query.Where(x => x.Time >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(x => x.Time <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(contributor))
        {
            var c = contributor.Trim().ToLower();
            query = query.Where(x => x.Contributor.ToLower() == c);
        }

        if (!string.IsNullOrWhiteSpace(company))
        {
            var comp = company.Trim().ToLower();
            query = query.Where(x => x.Company.ToLower() == comp);
        }

        var data = await query.ToListAsync();

        return new SummaryResult
        {
            TotalCommits = data.Sum(x => x.Commits),
            TotalPRs = data.Sum(x => x.PullRequests),
            TotalMerges = data.Sum(x => x.Merges),
            TotalMeetings = data.Sum(x => x.Meetings),
            TotalDocs = data.Sum(x => x.Documentation),

            ContributorCount = data
                .Select(x => x.Contributor)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };
    }
}

public class PRCycleTimeResult
{
    public DateTime Date { get; set; }
    public double AvgHours { get; set; }
}

public class SummaryResult
{
    public int TotalCommits { get; set; }
    public int TotalPRs { get; set; }
    public int TotalMerges { get; set; }
    public int TotalMeetings { get; set; }
    public int TotalDocs { get; set; }
    public int ContributorCount { get; set; }
}