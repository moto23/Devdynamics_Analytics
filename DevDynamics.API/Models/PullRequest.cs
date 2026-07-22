using System.ComponentModel.DataAnnotations.Schema;

namespace DevDynamics.API.Models;

/// <summary>
/// A pull request, with the lifecycle timestamps PR cycle time derives from.
///
/// Named "PullRequest" rather than a provider-neutral term because the concept
/// is universally understood; GitLab's "merge request" maps onto it directly.
/// </summary>
public class PullRequest
{
    public int Id { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Per-repository number — the "#123" users recognise.</summary>
    public int Number { get; set; }

    public int RepositoryId { get; set; }
    public TrackedRepository? Repository { get; set; }

    public int? ContributorId { get; set; }
    public Contributor? Contributor { get; set; }

    /// <summary>
    /// Kept alongside the FK so attribution survives even when the author
    /// cannot be resolved to a contributor record.
    /// </summary>
    public string? AuthorLogin { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>"open" or "closed" as reported by the provider.</summary>
    public string State { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Drives incremental sync. Pull requests mutate after creation, so syncing
    /// by this rather than CreatedAt is what stops merges on older PRs being
    /// missed permanently.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Set only when merged. Cycle time = MergedAt - CreatedAt.</summary>
    public DateTime? MergedAt { get; set; }

    /// <summary>Set when merged or rejected.</summary>
    public DateTime? ClosedAt { get; set; }

    [NotMapped]
    public bool IsMerged => MergedAt.HasValue;
}
