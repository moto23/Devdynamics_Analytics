using System.ComponentModel.DataAnnotations.Schema;

namespace DevDynamics.API.Models;

/// <summary>
/// A pull request with the lifecycle timestamps that PR cycle time is derived from.
/// </summary>
public class PullRequest
{
    public int Id { get; set; }

    /// <summary>GitHub's own numeric id for the pull request.</summary>
    public long GitHubId { get; set; }

    /// <summary>Per-repository PR number (the "#123" users see).</summary>
    public int Number { get; set; }

    public int RepositoryId { get; set; }
    public Repository? Repository { get; set; }

    public int? ContributorId { get; set; }
    public Contributor? Contributor { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>"open" or "closed" as reported by GitHub.</summary>
    public string State { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Set only for merged PRs. Cycle time = MergedAt - CreatedAt.</summary>
    public DateTime? MergedAt { get; set; }

    /// <summary>Set for both merged and rejected PRs.</summary>
    public DateTime? ClosedAt { get; set; }

    [NotMapped]
    public bool IsMerged => MergedAt.HasValue;
}
