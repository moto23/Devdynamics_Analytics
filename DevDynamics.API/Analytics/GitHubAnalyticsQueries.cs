using DevDynamics.API.Data;
using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Analytics;

public class RepositoryFilter
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>Contributor login. Null means all contributors.</summary>
    public string? Contributor { get; set; }

    /// <summary>Repository "owner/name". Null means all tracked repositories.</summary>
    public string? Repository { get; set; }

    /// <summary>Exclude automation accounts, which otherwise dominate the data.</summary>
    public bool ExcludeBots { get; set; }
}

public class CommitTrendPoint
{
    public DateTime Date { get; set; }
    public int Commits { get; set; }
}

public class PRCyclePoint
{
    public DateTime Date { get; set; }
    public double AvgHours { get; set; }
    public int MergedCount { get; set; }
}

public class ContributorSummary
{
    public string Login { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public bool IsBot { get; set; }
    public int Commits { get; set; }
}

public class GitHubSummary
{
    public int TotalCommits { get; set; }
    public int TotalPullRequests { get; set; }
    public int MergedPullRequests { get; set; }
    public int OpenPullRequests { get; set; }
    public int ContributorCount { get; set; }
    public int RepositoryCount { get; set; }
    public double AvgPrCycleHours { get; set; }
}

/// <summary>
/// Analytics over ingested source-control data.
///
/// Every query is scoped to *active* tracked repositories, so whichever
/// repositories are registered at the time are exactly what the dashboard
/// reports on — with no code change when repositories are added or removed.
///
/// Follows the Phase 2 performance rules: database-side aggregation,
/// AsNoTracking, projections, and sargable predicates (no function calls
/// wrapped around indexed columns).
/// </summary>
public class GitHubAnalyticsQueries(AppDbContext context, DatabaseProviderKind provider)
{
    private readonly AppDbContext _context = context;
    private readonly DatabaseProviderKind _provider = provider;

    private IQueryable<Commit> FilteredCommits(RepositoryFilter f)
    {
        var query = _context.Commits
            .AsNoTracking()
            .Where(c => c.Repository!.IsActive);

        if (f.StartDate.HasValue)
            query = query.Where(c => c.CommittedAt >= f.StartDate.Value);

        if (f.EndDate.HasValue)
            query = query.Where(c => c.CommittedAt <= f.EndDate.Value);

        if (!string.IsNullOrWhiteSpace(f.Repository))
        {
            var repo = f.Repository.Trim();
            query = query.Where(c => c.Repository!.FullName == repo);
        }

        if (!string.IsNullOrWhiteSpace(f.Contributor))
        {
            var login = f.Contributor.Trim();
            query = query.Where(c => c.Contributor!.Login == login);
        }

        if (f.ExcludeBots)
            query = query.Where(c => c.Contributor == null || !c.Contributor.IsBot);

        return query;
    }

    private IQueryable<PullRequest> FilteredPullRequests(RepositoryFilter f)
    {
        var query = _context.PullRequests
            .AsNoTracking()
            .Where(p => p.Repository!.IsActive);

        if (!string.IsNullOrWhiteSpace(f.Repository))
        {
            var repo = f.Repository.Trim();
            query = query.Where(p => p.Repository!.FullName == repo);
        }

        if (!string.IsNullOrWhiteSpace(f.Contributor))
        {
            var login = f.Contributor.Trim();
            query = query.Where(p => p.Contributor!.Login == login);
        }

        if (f.ExcludeBots)
            query = query.Where(p => p.Contributor == null || !p.Contributor.IsBot);

        return query;
    }

    /// <summary>
    /// Merged pull requests only, filtered on *merge* date. Cycle time is a
    /// property of merged work, so an open PR has no cycle time and a PR merged
    /// inside the window counts even if it was opened before it.
    /// </summary>
    private IQueryable<PullRequest> MergedInWindow(RepositoryFilter f)
    {
        var query = FilteredPullRequests(f).Where(p => p.MergedAt != null);

        if (f.StartDate.HasValue)
            query = query.Where(p => p.MergedAt >= f.StartDate.Value);

        if (f.EndDate.HasValue)
            query = query.Where(p => p.MergedAt <= f.EndDate.Value);

        return query;
    }

    // =====================================================================

    public async Task<List<CommitTrendPoint>> GetCommitTrendsAsync(RepositoryFilter f)
    {
        return await FilteredCommits(f)
            .GroupBy(c => c.CommittedAt.Date)
            .Select(g => new CommitTrendPoint
            {
                Date = g.Key,
                Commits = g.Count()
            })
            .OrderBy(p => p.Date)
            .ToListAsync();
    }

    public async Task<List<PRCyclePoint>> GetPRCycleTimeAsync(RepositoryFilter f)
    {
        var query = MergedInWindow(f);

        if (_provider == DatabaseProviderKind.SqlServer)
        {
            return await query
                .GroupBy(p => p.MergedAt!.Value.Date)
                .Select(g => new PRCyclePoint
                {
                    Date = g.Key,
                    MergedCount = g.Count(),
                    AvgHours = g.Average(p =>
                        EF.Functions.DateDiffMinute(p.CreatedAt, p.MergedAt!.Value) / 60.0)
                })
                .OrderBy(p => p.Date)
                .ToListAsync();
        }

        // SQLite has no DATEDIFF translation; project the two columns needed.
        var projected = await query
            .Select(p => new { p.CreatedAt, MergedAt = p.MergedAt!.Value })
            .ToListAsync();

        return projected
            .GroupBy(p => p.MergedAt.Date)
            .Select(g => new PRCyclePoint
            {
                Date = g.Key,
                MergedCount = g.Count(),
                AvgHours = g.Average(p => (p.MergedAt - p.CreatedAt).TotalHours)
            })
            .OrderBy(p => p.Date)
            .ToList();
    }

    public async Task<GitHubSummary> GetSummaryAsync(RepositoryFilter f)
    {
        var commits = FilteredCommits(f);
        var merged = MergedInWindow(f);

        var commitStats = await commits
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Contributors = g.Select(c => c.ContributorId).Distinct().Count(),
                Repositories = g.Select(c => c.RepositoryId).Distinct().Count()
            })
            .FirstOrDefaultAsync();

        var prStats = await FilteredPullRequests(f)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Merged = g.Count(p => p.MergedAt != null),
                Open = g.Count(p => p.State == "open")
            })
            .FirstOrDefaultAsync();

        var avgCycle = _provider == DatabaseProviderKind.SqlServer
            ? await merged
                .Select(p => (double?)(EF.Functions.DateDiffMinute(p.CreatedAt, p.MergedAt!.Value) / 60.0))
                .AverageAsync()
            : (await merged.Select(p => new { p.CreatedAt, M = p.MergedAt!.Value }).ToListAsync())
                .Select(p => (p.M - p.CreatedAt).TotalHours)
                .DefaultIfEmpty(0)
                .Average();

        return new GitHubSummary
        {
            TotalCommits = commitStats?.Total ?? 0,
            ContributorCount = commitStats?.Contributors ?? 0,
            RepositoryCount = commitStats?.Repositories ?? 0,
            TotalPullRequests = prStats?.Total ?? 0,
            MergedPullRequests = prStats?.Merged ?? 0,
            OpenPullRequests = prStats?.Open ?? 0,
            AvgPrCycleHours = Math.Round(avgCycle ?? 0, 2)
        };
    }

    /// <summary>
    /// Contributors present in the filtered data, ranked by commit count.
    /// Drives the contributor filter and is the basis of the Phase 4
    /// cross-contributor comparison.
    /// </summary>
    public async Task<List<ContributorSummary>> GetContributorsAsync(RepositoryFilter f)
    {
        return await FilteredCommits(f)
            .Where(c => c.Contributor != null)
            .GroupBy(c => new { c.Contributor!.Login, c.Contributor.AvatarUrl, c.Contributor.IsBot })
            .Select(g => new ContributorSummary
            {
                Login = g.Key.Login,
                AvatarUrl = g.Key.AvatarUrl,
                IsBot = g.Key.IsBot,
                Commits = g.Count()
            })
            .OrderByDescending(c => c.Commits)
            .ToListAsync();
    }
}
