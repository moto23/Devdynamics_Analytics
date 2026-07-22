using DevDynamics.API.Data;
using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Analytics;

/// <summary>
/// Analytics over ingested source-control data.
///
/// Every query is scoped to *active* tracked repositories, so whichever
/// repositories are registered at the time are exactly what is reported on,
/// with no code change when repositories are added or removed.
///
/// Performance rules held throughout: aggregate in the database, never in
/// application memory; AsNoTracking on every read; project only the columns the
/// caller needs; keep predicates sargable so the indexes are usable; and return
/// pages rather than whole collections.
/// </summary>
public class GitHubAnalyticsQueries(AppDbContext context, DatabaseProviderKind provider)
{
    private readonly AppDbContext _context = context;
    private readonly DatabaseProviderKind _provider = provider;

    private bool IsSqlServer => _provider == DatabaseProviderKind.SqlServer;

    // =====================================================================
    // Filter composition
    // =====================================================================

    private IQueryable<Commit> FilteredCommits(RepositoryFilter f)
    {
        var query = _context.Commits.AsNoTracking().Where(c => c.Repository!.IsActive);

        if (f.StartDate.HasValue) query = query.Where(c => c.CommittedAt >= f.StartDate.Value);
        if (f.EndDate.HasValue) query = query.Where(c => c.CommittedAt <= f.EndDate.Value);

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
        var query = _context.PullRequests.AsNoTracking().Where(p => p.Repository!.IsActive);

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
    /// Merged pull requests, filtered on *merge* date. Cycle time is a property
    /// of merged work: an open pull request has none, and one merged inside the
    /// window counts even if it was opened before it.
    /// </summary>
    private IQueryable<PullRequest> MergedInWindow(RepositoryFilter f)
    {
        var query = FilteredPullRequests(f).Where(p => p.MergedAt != null);

        if (f.StartDate.HasValue) query = query.Where(p => p.MergedAt >= f.StartDate.Value);
        if (f.EndDate.HasValue) query = query.Where(p => p.MergedAt <= f.EndDate.Value);

        return query;
    }

    /// <summary>Cycle time in hours, as a database-side expression.</summary>
    private IQueryable<double> CycleHours(IQueryable<PullRequest> merged) =>
        IsSqlServer
            ? merged.Select(p => EF.Functions.DateDiffMinute(p.CreatedAt, p.MergedAt!.Value) / 60.0)
            : merged.Select(p => (p.MergedAt!.Value - p.CreatedAt).TotalHours);

    // =====================================================================
    // Summary
    // =====================================================================

    public async Task<GitHubSummary> GetSummaryAsync(RepositoryFilter f)
    {
        var commitStats = await FilteredCommits(f)
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

        var percentiles = await CyclePercentilesAsync(MergedInWindow(f));

        return new GitHubSummary
        {
            TotalCommits = commitStats?.Total ?? 0,
            ContributorCount = commitStats?.Contributors ?? 0,
            RepositoryCount = commitStats?.Repositories ?? 0,
            TotalPullRequests = prStats?.Total ?? 0,
            MergedPullRequests = prStats?.Merged ?? 0,
            OpenPullRequests = prStats?.Open ?? 0,
            AvgPrCycleHours = percentiles.Mean,
            MedianPrCycleHours = percentiles.Median,
            P95PrCycleHours = percentiles.P95
        };
    }

    /// <summary>
    /// Percentiles need ordered values, so this pulls one projected double per
    /// merged pull request — never whole entities — and computes from that.
    /// Bounded by merged PRs in the window rather than by table size.
    /// </summary>
    private async Task<(double Mean, double Median, double P95)> CyclePercentilesAsync(
        IQueryable<PullRequest> merged)
    {
        var hours = await CycleHours(merged).OrderBy(h => h).ToListAsync();

        if (hours.Count == 0) return (0, 0, 0);

        return (
            Math.Round(hours.Average(), 2),
            Math.Round(Percentile(hours, 0.50), 2),
            Math.Round(Percentile(hours, 0.95), 2)
        );
    }

    private static double Percentile(List<double> sorted, double fraction)
    {
        if (sorted.Count == 1) return sorted[0];

        var index = (sorted.Count - 1) * fraction;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }

    // =====================================================================
    // Trends
    // =====================================================================

    public Task<List<CommitTrendPoint>> GetCommitTrendsAsync(RepositoryFilter f) =>
        FilteredCommits(f)
            .GroupBy(c => c.CommittedAt.Date)
            .Select(g => new CommitTrendPoint { Date = g.Key, Commits = g.Count() })
            .OrderBy(p => p.Date)
            .ToListAsync();

    public async Task<List<PRCyclePoint>> GetPRCycleTimeAsync(RepositoryFilter f)
    {
        var query = MergedInWindow(f);

        if (IsSqlServer)
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

    /// <summary>Daily commit counts for a calendar heatmap.</summary>
    public Task<List<HeatmapDay>> GetHeatmapAsync(RepositoryFilter f) =>
        FilteredCommits(f)
            .GroupBy(c => c.CommittedAt.Date)
            .Select(g => new HeatmapDay { Date = g.Key, Count = g.Count() })
            .OrderBy(d => d.Date)
            .ToListAsync();

    /// <summary>Weekday × hour distribution, grouped in SQL.</summary>
    public async Task<List<ActivityCell>> GetActivityDistributionAsync(RepositoryFilter f)
    {
        var commits = FilteredCommits(f);

        if (IsSqlServer)
        {
            // DateTime.DayOfWeek has no SQL translation, so the weekday is
            // derived arithmetically instead: 1900-01-07 was a Sunday, so the
            // day count since then, modulo 7, yields 0 = Sunday. This keeps the
            // grouping in the database rather than pulling every commit date
            // into memory to bucket it.
            var epochSunday = new DateTime(1900, 1, 7);

            var rows = await commits
                .GroupBy(c => new
                {
                    Day = EF.Functions.DateDiffDay(epochSunday, c.CommittedAt) % 7,
                    Hour = c.CommittedAt.Hour
                })
                .Select(g => new { g.Key.Day, g.Key.Hour, Count = g.Count() })
                .ToListAsync();

            return rows
                .Select(r => new ActivityCell
                {
                    // Guard against a negative modulo for any pre-epoch date.
                    DayOfWeek = ((r.Day % 7) + 7) % 7,
                    Hour = r.Hour,
                    Count = r.Count
                })
                .OrderBy(c => c.DayOfWeek).ThenBy(c => c.Hour)
                .ToList();
        }

        var dates = await commits.Select(c => c.CommittedAt).ToListAsync();

        return dates
            .GroupBy(d => new { Day = (int)d.DayOfWeek, d.Hour })
            .Select(g => new ActivityCell { DayOfWeek = g.Key.Day, Hour = g.Key.Hour, Count = g.Count() })
            .OrderBy(c => c.DayOfWeek).ThenBy(c => c.Hour)
            .ToList();
    }

    // =====================================================================
    // Contributors
    // =====================================================================

    /// <summary>
    /// Ranked contributor statistics, paginated.
    ///
    /// Commits, pull requests and repository counts are three separate
    /// aggregates, deliberately issued as three grouped queries and joined in
    /// memory over the page's contributors. Correlated subqueries per row would
    /// be the N+1 shape this avoids.
    /// </summary>
    public async Task<Connection<ContributorStats>> GetContributorsAsync(
        RepositoryFilter f,
        string? search,
        string sortBy,
        bool descending,
        string? after,
        int? first)
    {
        var pageSize = Cursor.PageSize(first);
        var offset = Cursor.DecodeOffset(after);

        var commits = FilteredCommits(f).Where(c => c.Contributor != null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            commits = commits.Where(c =>
                c.Contributor!.Login.Contains(term) ||
                (c.Contributor.Name != null && c.Contributor.Name.Contains(term)));
        }

        // One grouped query for the commit-side aggregates.
        var baseQuery = commits
            .GroupBy(c => c.ContributorId)
            .Select(g => new
            {
                ContributorId = g.Key!.Value,
                Commits = g.Count(),
                Repositories = g.Select(c => c.RepositoryId).Distinct().Count(),
                First = g.Min(c => c.CommittedAt),
                Last = g.Max(c => c.CommittedAt)
            });

        var totalCount = await baseQuery.CountAsync();

        baseQuery = (sortBy?.ToLowerInvariant()) switch
        {
            "repositories" => descending
                ? baseQuery.OrderByDescending(x => x.Repositories).ThenBy(x => x.ContributorId)
                : baseQuery.OrderBy(x => x.Repositories).ThenBy(x => x.ContributorId),
            "recent" => descending
                ? baseQuery.OrderByDescending(x => x.Last).ThenBy(x => x.ContributorId)
                : baseQuery.OrderBy(x => x.Last).ThenBy(x => x.ContributorId),
            _ => descending
                ? baseQuery.OrderByDescending(x => x.Commits).ThenBy(x => x.ContributorId)
                : baseQuery.OrderBy(x => x.Commits).ThenBy(x => x.ContributorId)
        };

        var page = await baseQuery.Skip(offset).Take(pageSize).ToListAsync();

        if (page.Count == 0)
        {
            return new Connection<ContributorStats>
            {
                PageInfo = new PageInfo { TotalCount = totalCount, HasPreviousPage = offset > 0 }
            };
        }

        var ids = page.Select(p => p.ContributorId).ToList();

        // Identities for the page only.
        var people = await _context.Contributors.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Login, c.Name, c.AvatarUrl, c.HtmlUrl, c.IsBot })
            .ToDictionaryAsync(c => c.Id);

        // Pull request aggregates for the page's contributors, in one query.
        var prAggregates = await FilteredPullRequests(f)
            .Where(p => p.ContributorId != null && ids.Contains(p.ContributorId.Value))
            .GroupBy(p => p.ContributorId!.Value)
            .Select(g => new
            {
                ContributorId = g.Key,
                Opened = g.Count(),
                Merged = g.Count(p => p.MergedAt != null)
            })
            .ToDictionaryAsync(x => x.ContributorId);

        var items = page.Select(row =>
        {
            people.TryGetValue(row.ContributorId, out var person);
            prAggregates.TryGetValue(row.ContributorId, out var pr);

            var opened = pr?.Opened ?? 0;
            var merged = pr?.Merged ?? 0;

            return new ContributorStats
            {
                Login = person?.Login ?? "unknown",
                Name = person?.Name,
                AvatarUrl = person?.AvatarUrl,
                HtmlUrl = person?.HtmlUrl,
                IsBot = person?.IsBot ?? false,
                Commits = row.Commits,
                RepositoryCount = row.Repositories,
                PullRequestsOpened = opened,
                PullRequestsMerged = merged,
                FirstActivityUtc = row.First,
                LastActivityUtc = row.Last,
                Score = ComputeScore(row.Commits, opened, merged, row.Repositories)
            };
        }).ToList();

        return new Connection<ContributorStats>
        {
            Items = items,
            PageInfo = new PageInfo
            {
                TotalCount = totalCount,
                HasNextPage = offset + page.Count < totalCount,
                HasPreviousPage = offset > 0,
                EndCursor = Cursor.EncodeOffset(offset + page.Count)
            }
        };
    }

    /// <summary>
    /// Weighted activity score. Merged pull requests weigh most because they
    /// represent work that survived review; breadth across repositories adds a
    /// small amount. The inputs are returned alongside it so the number can be
    /// explained rather than taken on trust.
    /// </summary>
    private static double ComputeScore(int commits, int prsOpened, int prsMerged, int repositories) =>
        Math.Round(commits * 1.0 + prsOpened * 2.0 + prsMerged * 3.0 + repositories * 5.0, 1);

    public async Task<ContributorDetail?> GetContributorDetailAsync(string login, RepositoryFilter f)
    {
        var person = await _context.Contributors.AsNoTracking()
            .Where(c => c.Login == login)
            .Select(c => new { c.Id, c.Login, c.Name, c.AvatarUrl, c.HtmlUrl, c.IsBot })
            .FirstOrDefaultAsync();

        if (person is null) return null;

        var scoped = new RepositoryFilter
        {
            StartDate = f.StartDate,
            EndDate = f.EndDate,
            Repository = f.Repository,
            Contributor = login,
            ExcludeBots = false
        };

        var commits = FilteredCommits(scoped);

        var totals = await commits
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Commits = g.Count(),
                Repositories = g.Select(c => c.RepositoryId).Distinct().Count(),
                First = g.Min(c => c.CommittedAt),
                Last = g.Max(c => c.CommittedAt)
            })
            .FirstOrDefaultAsync();

        var prs = await FilteredPullRequests(scoped)
            .GroupBy(_ => 1)
            .Select(g => new { Opened = g.Count(), Merged = g.Count(p => p.MergedAt != null) })
            .FirstOrDefaultAsync();

        var perRepository = await commits
            .GroupBy(c => c.Repository!.FullName)
            .Select(g => new RepositoryContribution { FullName = g.Key, Commits = g.Count() })
            .OrderByDescending(r => r.Commits)
            .ToListAsync();

        var heatmap = await commits
            .GroupBy(c => c.CommittedAt.Date)
            .Select(g => new HeatmapDay { Date = g.Key, Count = g.Count() })
            .OrderBy(d => d.Date)
            .ToListAsync();

        return new ContributorDetail
        {
            Stats = new ContributorStats
            {
                Login = person.Login,
                Name = person.Name,
                AvatarUrl = person.AvatarUrl,
                HtmlUrl = person.HtmlUrl,
                IsBot = person.IsBot,
                Commits = totals?.Commits ?? 0,
                RepositoryCount = totals?.Repositories ?? 0,
                PullRequestsOpened = prs?.Opened ?? 0,
                PullRequestsMerged = prs?.Merged ?? 0,
                FirstActivityUtc = totals?.First,
                LastActivityUtc = totals?.Last,
                Score = ComputeScore(totals?.Commits ?? 0, prs?.Opened ?? 0, prs?.Merged ?? 0, totals?.Repositories ?? 0)
            },
            Repositories = perRepository,
            Heatmap = heatmap
        };
    }

    // =====================================================================
    // Repository insights
    // =====================================================================

    public async Task<RepositoryHealth?> GetRepositoryHealthAsync(string fullName, RepositoryFilter f)
    {
        var scoped = new RepositoryFilter
        {
            StartDate = f.StartDate,
            EndDate = f.EndDate,
            Repository = fullName,
            ExcludeBots = f.ExcludeBots
        };

        var exists = await _context.TrackedRepositories.AsNoTracking()
            .AnyAsync(r => r.FullName == fullName);

        if (!exists) return null;

        var commits = FilteredCommits(scoped);

        var commitStats = await commits
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Contributors = g.Select(c => c.ContributorId).Distinct().Count(),
                Days = g.Select(c => c.CommittedAt.Date).Distinct().Count(),
                Last = g.Max(c => c.CommittedAt),
                First = g.Min(c => c.CommittedAt)
            })
            .FirstOrDefaultAsync();

        var prStats = await FilteredPullRequests(scoped)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Merged = g.Count(p => p.MergedAt != null),
                Open = g.Count(p => p.State == "open")
            })
            .FirstOrDefaultAsync();

        var oldestOpen = await FilteredPullRequests(scoped)
            .Where(p => p.State == "open")
            .OrderBy(p => p.CreatedAt)
            .Select(p => (DateTime?)p.CreatedAt)
            .FirstOrDefaultAsync();

        var topContributorCommits = await commits
            .Where(c => c.ContributorId != null)
            .GroupBy(c => c.ContributorId)
            .Select(g => g.Count())
            .OrderByDescending(n => n)
            .FirstOrDefaultAsync();

        var percentiles = await CyclePercentilesAsync(MergedInWindow(scoped));

        var total = commitStats?.Total ?? 0;

        var weeks = commitStats is null || commitStats.Total == 0
            ? 0
            : Math.Max(1, (commitStats.Last - commitStats.First).TotalDays / 7.0);

        return new RepositoryHealth
        {
            FullName = fullName,
            Commits = total,
            Contributors = commitStats?.Contributors ?? 0,
            ActiveDays = commitStats?.Days ?? 0,
            LastCommitUtc = commitStats?.Last,
            CommitsPerWeek = weeks > 0 ? Math.Round(total / weeks, 1) : 0,
            TopContributorShare = total > 0 ? Math.Round((double)topContributorCommits / total, 3) : 0,
            PullRequests = prStats?.Total ?? 0,
            MergedPullRequests = prStats?.Merged ?? 0,
            OpenPullRequests = prStats?.Open ?? 0,
            MergeRate = prStats is { Total: > 0 } ? Math.Round((double)prStats.Merged / prStats.Total, 3) : 0,
            MedianCycleHours = percentiles.Median,
            P95CycleHours = percentiles.P95,
            OldestOpenPullRequestUtc = oldestOpen
        };
    }

    public async Task<List<LanguageSlice>> GetLanguageDistributionAsync(string? repositoryFullName)
    {
        var query = _context.RepositoryLanguages.AsNoTracking()
            .Where(l => l.Repository!.IsActive);

        if (!string.IsNullOrWhiteSpace(repositoryFullName))
        {
            var name = repositoryFullName.Trim();
            query = query.Where(l => l.Repository!.FullName == name);
        }

        var totals = await query
            .GroupBy(l => l.Language)
            .Select(g => new { Language = g.Key, Bytes = g.Sum(x => x.Bytes) })
            .OrderByDescending(x => x.Bytes)
            .ToListAsync();

        var grandTotal = totals.Sum(t => t.Bytes);
        if (grandTotal == 0) return [];

        return totals.Select(t => new LanguageSlice
        {
            Language = t.Language,
            Bytes = t.Bytes,
            Percentage = Math.Round((double)t.Bytes / grandTotal * 100, 2)
        }).ToList();
    }

    // =====================================================================
    // Paginated entity lists — keyset, so depth does not degrade
    // =====================================================================

    public async Task<Connection<Commit>> GetCommitsAsync(RepositoryFilter f, string? after, int? first)
    {
        var pageSize = Cursor.PageSize(first, 25);
        var query = FilteredCommits(f);

        var keyset = Cursor.DecodeKeyset(after);
        if (keyset is not null)
        {
            var (date, id) = keyset.Value;
            // Strictly after the last row in the previous page, in the same
            // (CommittedAt desc, Id desc) order the index supports.
            query = query.Where(c => c.CommittedAt < date || (c.CommittedAt == date && c.Id < id));
        }

        var items = await query
            .OrderByDescending(c => c.CommittedAt).ThenByDescending(c => c.Id)
            .Take(pageSize + 1)
            .Include(c => c.Contributor)
            .Include(c => c.Repository)
            .ToListAsync();

        var hasNext = items.Count > pageSize;
        if (hasNext) items.RemoveAt(items.Count - 1);

        return new Connection<Commit>
        {
            Items = items,
            PageInfo = new PageInfo
            {
                HasNextPage = hasNext,
                HasPreviousPage = after is not null,
                EndCursor = items.Count > 0
                    ? Cursor.EncodeKeyset(items[^1].CommittedAt, items[^1].Id)
                    : null
            }
        };
    }

    public async Task<Connection<PullRequest>> GetPullRequestsAsync(
        RepositoryFilter f, string? state, string? after, int? first)
    {
        var pageSize = Cursor.PageSize(first, 25);
        var query = FilteredPullRequests(f);

        if (!string.IsNullOrWhiteSpace(state))
        {
            var wanted = state.Trim().ToLowerInvariant();
            query = wanted switch
            {
                "merged" => query.Where(p => p.MergedAt != null),
                "open" => query.Where(p => p.State == "open"),
                "closed" => query.Where(p => p.State == "closed" && p.MergedAt == null),
                _ => query
            };
        }

        if (f.StartDate.HasValue) query = query.Where(p => p.UpdatedAt >= f.StartDate.Value);
        if (f.EndDate.HasValue) query = query.Where(p => p.UpdatedAt <= f.EndDate.Value);

        var keyset = Cursor.DecodeKeyset(after);
        if (keyset is not null)
        {
            var (date, id) = keyset.Value;
            query = query.Where(p => p.UpdatedAt < date || (p.UpdatedAt == date && p.Id < id));
        }

        var items = await query
            .OrderByDescending(p => p.UpdatedAt).ThenByDescending(p => p.Id)
            .Take(pageSize + 1)
            .Include(p => p.Contributor)
            .Include(p => p.Repository)
            .ToListAsync();

        var hasNext = items.Count > pageSize;
        if (hasNext) items.RemoveAt(items.Count - 1);

        return new Connection<PullRequest>
        {
            Items = items,
            PageInfo = new PageInfo
            {
                HasNextPage = hasNext,
                HasPreviousPage = after is not null,
                EndCursor = items.Count > 0
                    ? Cursor.EncodeKeyset(items[^1].UpdatedAt, items[^1].Id)
                    : null
            }
        };
    }

    public async Task<Connection<SyncRun>> GetSyncRunsAsync(int repositoryId, string? after, int? first)
    {
        var pageSize = Cursor.PageSize(first, 20);

        var query = _context.SyncRuns.AsNoTracking().Where(r => r.RepositoryId == repositoryId);

        var keyset = Cursor.DecodeKeyset(after);
        if (keyset is not null)
        {
            var (date, id) = keyset.Value;
            query = query.Where(r => r.StartedAtUtc < date || (r.StartedAtUtc == date && r.Id < id));
        }

        var items = await query
            .OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id)
            .Take(pageSize + 1)
            .ToListAsync();

        var hasNext = items.Count > pageSize;
        if (hasNext) items.RemoveAt(items.Count - 1);

        return new Connection<SyncRun>
        {
            Items = items,
            PageInfo = new PageInfo
            {
                HasNextPage = hasNext,
                HasPreviousPage = after is not null,
                EndCursor = items.Count > 0
                    ? Cursor.EncodeKeyset(items[^1].StartedAtUtc, items[^1].Id)
                    : null
            }
        };
    }
}
