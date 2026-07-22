namespace DevDynamics.API.SourceControl.GitHub;

/// <summary>
/// Ingestion tuning. Every value is configuration-driven: there are no
/// repository names, and no repository count limit, anywhere in the codebase.
/// </summary>
public class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>
    /// Supplied via the GitHub__Token environment variable (or user-secrets
    /// locally). Never committed.
    /// </summary>
    public string? Token { get; set; }

    public string ApiBaseUrl { get; set; } = "https://api.github.com/";

    /// <summary>Sent on every request; GitHub rejects requests without one.</summary>
    public string UserAgent { get; set; } = "DevDynamics-Analytics";

    /// <summary>How far back a first-time sync reaches.</summary>
    public int SyncWindowDays { get; set; } = 90;

    /// <summary>Bounds the work per resource, per repository, per run.</summary>
    public int MaxPagesPerResource { get; set; } = 20;

    /// <summary>GitHub's maximum is 100.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// Stop syncing while fewer than this many API requests remain, leaving
    /// headroom for interactive requests such as validating a new repository.
    /// </summary>
    public int RateLimitFloor { get; set; } = 100;

    /// <summary>
    /// Re-request a small window before the last cursor to absorb clock skew
    /// and in-flight writes. Safe because upserts are idempotent.
    /// </summary>
    public int OverlapBufferMinutes { get; set; } = 60;

    public int MaxRetries { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 30;
}
