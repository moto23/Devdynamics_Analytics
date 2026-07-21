using DevDynamics.API.Data;
using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Analytics;

public class AnalyticsFilter
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Contributor { get; set; }
    public string? Company { get; set; }
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

/// <summary>
/// Analytics query implementations.
///
/// Both the original ("naive") and current ("optimized") versions live here on
/// purpose: the benchmark endpoint executes these exact methods, so the
/// published before/after numbers measure the real production code path rather
/// than a reimplementation written to flatter the result.
///
/// The naive versions are dead code in the serving path and exist only as the
/// benchmark baseline.
/// </summary>
public class AnalyticsQueries(AppDbContext context, DatabaseProviderKind provider)
{
    private readonly AppDbContext _context = context;
    private readonly DatabaseProviderKind _provider = provider;

    // =====================================================================
    // Shared filtering
    // =====================================================================

    /// <summary>
    /// BASELINE filter. Uses ToLower() on both sides of the comparison, which
    /// wraps the column in a function call and makes the predicate
    /// non-sargable: SQL Server cannot seek an index and falls back to a scan.
    /// </summary>
    private IQueryable<DevActivity> NaiveFiltered(AnalyticsFilter f)
    {
        var query = _context.DevActivities.AsQueryable();

        if (f.StartDate.HasValue)
            query = query.Where(x => x.Time >= f.StartDate.Value);

        if (f.EndDate.HasValue)
            query = query.Where(x => x.Time <= f.EndDate.Value);

        if (!string.IsNullOrWhiteSpace(f.Contributor))
        {
            var c = f.Contributor.Trim().ToLower();
            query = query.Where(x => x.Contributor.ToLower() == c);
        }

        if (!string.IsNullOrWhiteSpace(f.Company))
        {
            var comp = f.Company.Trim().ToLower();
            query = query.Where(x => x.Company.ToLower() == comp);
        }

        return query;
    }

    /// <summary>
    /// OPTIMIZED filter. Plain equality keeps the predicate sargable so the
    /// composite indexes on (Contributor, Time) and (Company, Time) are usable.
    /// Case-insensitivity comes from the database collation instead of ToLower():
    /// SQL Server's default (SQL_Latin1_General_CP1_CI_AS) is already
    /// case-insensitive, so behaviour is preserved without defeating the index.
    /// AsNoTracking() skips change-tracker bookkeeping for read-only analytics.
    /// </summary>
    private IQueryable<DevActivity> OptimizedFiltered(AnalyticsFilter f)
    {
        var query = _context.DevActivities.AsNoTracking();

        if (f.StartDate.HasValue)
            query = query.Where(x => x.Time >= f.StartDate.Value);

        if (f.EndDate.HasValue)
            query = query.Where(x => x.Time <= f.EndDate.Value);

        if (!string.IsNullOrWhiteSpace(f.Contributor))
        {
            var c = f.Contributor.Trim();
            query = query.Where(x => x.Contributor == c);
        }

        if (!string.IsNullOrWhiteSpace(f.Company))
        {
            var comp = f.Company.Trim();
            query = query.Where(x => x.Company == comp);
        }

        return query;
    }

    // =====================================================================
    // Summary statistics
    // =====================================================================

    /// <summary>
    /// BASELINE: materialises every matching row over the wire, then sums them
    /// in application memory. Cost grows with the number of matching rows.
    /// </summary>
    public async Task<SummaryResult> NaiveSummaryStatsAsync(AnalyticsFilter f)
    {
        var data = await NaiveFiltered(f).ToListAsync();

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

    /// <summary>
    /// OPTIMIZED: aggregation runs inside the database, in a SINGLE round trip.
    ///
    /// The single round trip matters as much as the aggregation. An earlier
    /// version issued two queries (sums, then COUNT DISTINCT) and measured
    /// *slower* than the naive version on highly selective filters: when a
    /// filter matches few rows the naive version costs one round trip, so a
    /// two-round-trip "optimization" loses on network latency alone. Both
    /// aggregates are therefore projected from one GroupBy.
    /// </summary>
    public async Task<SummaryResult> OptimizedSummaryStatsAsync(AnalyticsFilter f)
    {
        var result = await OptimizedFiltered(f)
            .GroupBy(_ => 1)
            .Select(g => new SummaryResult
            {
                TotalCommits = g.Sum(x => x.Commits),
                TotalPRs = g.Sum(x => x.PullRequests),
                TotalMerges = g.Sum(x => x.Merges),
                TotalMeetings = g.Sum(x => x.Meetings),
                TotalDocs = g.Sum(x => x.Documentation),
                ContributorCount = g.Select(x => x.Contributor).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        // No matching rows: GroupBy yields nothing, so return an empty summary.
        return result ?? new SummaryResult();
    }

    // =====================================================================
    // PR cycle time
    // =====================================================================

    /// <summary>
    /// BASELINE: pulls all matching rows, then groups and averages in memory.
    /// </summary>
    public async Task<List<PRCycleTimeResult>> NaivePRCycleTimeAsync(AnalyticsFilter f)
    {
        var data = await NaiveFiltered(f).ToListAsync();

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

    /// <summary>
    /// OPTIMIZED: GROUP BY and AVG execute in the database, returning one row
    /// per day rather than one row per activity.
    /// </summary>
    public async Task<List<PRCycleTimeResult>> OptimizedPRCycleTimeAsync(AnalyticsFilter f)
    {
        var query = OptimizedFiltered(f);

        if (_provider == DatabaseProviderKind.SqlServer)
        {
            return await query
                .GroupBy(x => x.Time.Date)
                .Select(g => new PRCycleTimeResult
                {
                    Date = g.Key,
                    // DATEDIFF in minutes keeps sub-hour precision; TimeSpan
                    // subtraction has no SQL translation.
                    AvgHours = g.Average(a =>
                        EF.Functions.DateDiffMinute(a.PROpenedAt, a.PRMergedAt) / 60.0)
                })
                .OrderBy(x => x.Date)
                .ToListAsync();
        }

        // SQLite fallback: no DATEDIFF translation, so project the three needed
        // columns and finish in memory. Still far cheaper than loading entities.
        var projected = await query
            .Select(x => new { x.Time, x.PROpenedAt, x.PRMergedAt })
            .ToListAsync();

        return projected
            .GroupBy(x => x.Time.Date)
            .OrderBy(g => g.Key)
            .Select(g => new PRCycleTimeResult
            {
                Date = g.Key,
                AvgHours = g.Average(a => (a.PRMergedAt - a.PROpenedAt).TotalHours)
            })
            .ToList();
    }

    // =====================================================================
    // Activity list
    // =====================================================================

    public async Task<List<DevActivity>> OptimizedDevActivitiesAsync(AnalyticsFilter f)
    {
        return await OptimizedFiltered(f)
            .OrderBy(x => x.Time)
            .ToListAsync();
    }
}
