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

public class GitHubSummary
{
    public int TotalCommits { get; set; }
    public int TotalPullRequests { get; set; }
    public int MergedPullRequests { get; set; }
    public int OpenPullRequests { get; set; }
    public int ContributorCount { get; set; }
    public int RepositoryCount { get; set; }
    public double AvgPrCycleHours { get; set; }

    /// <summary>
    /// Median is the headline figure for cycle time: a single long-lived pull
    /// request skews the mean badly, so the mean alone misrepresents a typical
    /// review.
    /// </summary>
    public double MedianPrCycleHours { get; set; }
    public double P95PrCycleHours { get; set; }
}

/// <summary>One contributor's activity within the current filter.</summary>
public class ContributorStats
{
    public string Login { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public string? HtmlUrl { get; set; }
    public bool IsBot { get; set; }

    public int Commits { get; set; }
    public int PullRequestsOpened { get; set; }
    public int PullRequestsMerged { get; set; }
    public int RepositoryCount { get; set; }

    public DateTime? FirstActivityUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }

    /// <summary>
    /// A weighted activity score, exposed alongside its inputs so it can be
    /// explained rather than trusted blindly. Merged pull requests weigh most
    /// because they represent work that landed after review.
    /// </summary>
    public double Score { get; set; }
}

public class ContributorDetail
{
    public ContributorStats Stats { get; set; } = new();
    public List<RepositoryContribution> Repositories { get; set; } = [];
    public List<HeatmapDay> Heatmap { get; set; } = [];
}

public class RepositoryContribution
{
    public string FullName { get; set; } = string.Empty;
    public int Commits { get; set; }
}

public class HeatmapDay
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

/// <summary>Commits bucketed by weekday and hour — the "punchcard" view.</summary>
public class ActivityCell
{
    /// <summary>0 = Sunday.</summary>
    public int DayOfWeek { get; set; }
    public int Hour { get; set; }
    public int Count { get; set; }
}

public class LanguageSlice
{
    public string Language { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public double Percentage { get; set; }
}

/// <summary>
/// Repository health.
///
/// Every metric here is computed from ingested data. Deliberately absent:
/// time-to-first-review (needs the reviews endpoint), lines changed (one API
/// call per commit) and issue metrics (not ingested). They are omitted rather
/// than approximated.
/// </summary>
public class RepositoryHealth
{
    public string FullName { get; set; } = string.Empty;

    public int Commits { get; set; }
    public int PullRequests { get; set; }
    public int MergedPullRequests { get; set; }
    public int OpenPullRequests { get; set; }

    /// <summary>Merged as a share of all pull requests seen in the window.</summary>
    public double MergeRate { get; set; }

    public double MedianCycleHours { get; set; }
    public double P95CycleHours { get; set; }

    public int ActiveDays { get; set; }
    public double CommitsPerWeek { get; set; }

    public int Contributors { get; set; }

    /// <summary>
    /// Share of commits by the single busiest contributor. High values mean the
    /// repository depends on one person — the "bus factor" signal.
    /// </summary>
    public double TopContributorShare { get; set; }

    public DateTime? OldestOpenPullRequestUtc { get; set; }
    public DateTime? LastCommitUtc { get; set; }
}
