namespace DevDynamics.API.Models;

/// <summary>
/// A GitHub repository tracked by the dashboard.
/// </summary>
public class Repository
{
    public int Id { get; set; }

    /// <summary>GitHub's own numeric id for the repository.</summary>
    public long GitHubId { get; set; }

    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"owner/name", the canonical GitHub identifier.</summary>
    public string FullName { get; set; } = string.Empty;

    public string? Description { get; set; }
    public string? Language { get; set; }
    public string? HtmlUrl { get; set; }
    public string? DefaultBranch { get; set; }

    public int StarCount { get; set; }

    /// <summary>Last successful ingestion run; drives incremental sync.</summary>
    public DateTime? LastSyncedAt { get; set; }

    public List<Commit> Commits { get; set; } = [];
    public List<PullRequest> PullRequests { get; set; } = [];
}
