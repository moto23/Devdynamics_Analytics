namespace DevDynamics.API.Models;

/// <summary>
/// One synchronisation attempt.
///
/// The tracked repository holds only the latest outcome; this is the history,
/// so a user can see whether a repository has been failing repeatedly or simply
/// had one bad run.
/// </summary>
public class SyncRun
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }
    public TrackedRepository? Repository { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Matches SyncStatuses.</summary>
    public string Status { get; set; } = string.Empty;

    public int CommitsIngested { get; set; }
    public int PullRequestsIngested { get; set; }
    public int ContributorsAdded { get; set; }

    /// <summary>True when a page or rate-limit budget stopped the run early.</summary>
    public bool Truncated { get; set; }

    public int DurationMs { get; set; }

    public string? Error { get; set; }

    /// <summary>"Manual", "Scheduled" or "Startup" — why the run happened.</summary>
    public string Trigger { get; set; } = "Manual";
}

public static class SyncTriggers
{
    public const string Manual = "Manual";
    public const string Scheduled = "Scheduled";
    public const string Startup = "Startup";
}
