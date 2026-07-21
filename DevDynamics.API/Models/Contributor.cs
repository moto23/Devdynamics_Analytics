namespace DevDynamics.API.Models;

/// <summary>
/// A GitHub user who has authored commits or pull requests in a tracked repository.
/// </summary>
public class Contributor
{
    public int Id { get; set; }

    /// <summary>GitHub's own numeric id for the user.</summary>
    public long GitHubId { get; set; }

    public string Login { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public string? HtmlUrl { get; set; }

    public List<Commit> Commits { get; set; } = [];
    public List<PullRequest> PullRequests { get; set; } = [];
}
