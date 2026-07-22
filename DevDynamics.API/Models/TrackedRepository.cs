namespace DevDynamics.API.Models;

/// <summary>
/// Sync lifecycle of a tracked repository. Persisted as a string so the values
/// stay readable in the database and adding a state is not a breaking change.
/// </summary>
public static class SyncStatuses
{
    public const string NeverSynced = "NeverSynced";
    public const string Queued = "Queued";
    public const string Syncing = "Syncing";
    public const string Succeeded = "Succeeded";
    public const string PartiallySynced = "PartiallySynced";
    public const string Failed = "Failed";
}

/// <summary>
/// A repository the platform ingests and reports on.
///
/// This is the registry: the sync worker reads exclusively from this table, so
/// repositories are added, removed, enabled and disabled at runtime without any
/// code change. Nothing anywhere hardcodes a repository name or a repository
/// count.
///
/// Deliberately provider-neutral. GitHub is the only implemented provider today,
/// but nothing in this model, in the sync workflow, or in analytics assumes it.
/// </summary>
public class TrackedRepository
{
    public int Id { get; set; }

    /// <summary>Source control provider key, e.g. "GitHub". See ISourceControlProvider.</summary>
    public string Provider { get; set; } = "GitHub";

    /// <summary>
    /// The provider's own identifier, as a string so any provider's id shape fits.
    /// Survives repository renames, unlike FullName.
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"owner/name" — how users refer to the repository.</summary>
    public string FullName { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? Language { get; set; }
    public string? HtmlUrl { get; set; }
    public string? DefaultBranch { get; set; }

    public int StarCount { get; set; }
    public int ForkCount { get; set; }
    public int OpenIssueCount { get; set; }
    public bool IsPrivate { get; set; }

    // =====================================================================
    // User-managed metadata
    // =====================================================================

    /// <summary>Optional display name, for when "owner/name" is not how a team refers to it.</summary>
    public string? Nickname { get; set; }

    /// <summary>Free-text note shown alongside the repository.</summary>
    public string? Notes { get; set; }

    /// <summary>Pinned repositories sort ahead of the rest.</summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// How often the scheduler may re-sync this repository. Null falls back to
    /// the global interval; zero or less excludes it from scheduled syncs
    /// without disabling it for manual ones.
    /// </summary>
    public int? SyncIntervalMinutes { get; set; }

    // =====================================================================
    // Lifecycle
    // =====================================================================

    /// <summary>
    /// Inactive repositories are skipped by the sync worker and excluded from
    /// analytics, without losing already-ingested history.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Marks rows created by the demo seeder. Purely informational — demo rows
    /// are ordinary records and can be removed like any other.
    /// </summary>
    public bool IsDemoData { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
    public string? AddedBy { get; set; }

    // =====================================================================
    // Sync state
    // =====================================================================

    public string SyncStatus { get; set; } = SyncStatuses.NeverSynced;

    public DateTime? LastSyncStartedAtUtc { get; set; }
    public DateTime? LastSyncCompletedAtUtc { get; set; }
    public string? LastSyncError { get; set; }
    public int? LastSyncDurationMs { get; set; }

    // =====================================================================
    // Incremental cursors
    // =====================================================================

    /// <summary>
    /// Commits are immutable, so this is a high-water mark: only commits newer
    /// than this are requested. Advanced only after a successful page.
    /// </summary>
    public DateTime? CommitsSyncedThroughUtc { get; set; }

    /// <summary>
    /// Pull requests mutate — an open PR is later merged — so this tracks the
    /// last seen *update* time, not creation time. Syncing by creation date
    /// would permanently miss merges on older PRs.
    /// </summary>
    public DateTime? PullsSyncedThroughUtc { get; set; }

    /// <summary>
    /// Opaque provider-defined change token (an HTTP ETag for GitHub). Lets a
    /// provider answer "nothing changed" cheaply; for GitHub a 304 response
    /// does not count against the rate limit.
    /// </summary>
    public string? CommitsSyncToken { get; set; }
    public string? PullsSyncToken { get; set; }

    // =====================================================================
    // Denormalised counters (display only; analytics always query the facts)
    // =====================================================================

    public int TotalCommits { get; set; }
    public int TotalPullRequests { get; set; }
    public int TotalContributors { get; set; }

    public List<Commit> Commits { get; set; } = [];
    public List<PullRequest> PullRequests { get; set; } = [];
    public List<RepositoryLanguage> Languages { get; set; } = [];
    public List<SyncRun> SyncRuns { get; set; } = [];
}
