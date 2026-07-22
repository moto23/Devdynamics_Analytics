namespace DevDynamics.API.Models;

/// <summary>
/// A person (or bot) who authored commits or pull requests in a tracked
/// repository. Identity is global across repositories, so the same contributor
/// working in several repositories is one row.
/// </summary>
public class Contributor
{
    public int Id { get; set; }

    public string Provider { get; set; } = "GitHub";

    /// <summary>The provider's stable user id. Survives login changes.</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public string? HtmlUrl { get; set; }

    /// <summary>
    /// True for automation accounts such as dependabot. Kept as a flag rather
    /// than discarded at ingestion: bots are real activity, but they dominate
    /// contributor comparisons, so analytics needs to be able to exclude them.
    /// </summary>
    public bool IsBot { get; set; }

    public List<Commit> Commits { get; set; } = [];
    public List<PullRequest> PullRequests { get; set; } = [];
}
