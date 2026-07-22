namespace DevDynamics.API.Models;

/// <summary>
/// A commit on a tracked repository's default branch.
/// </summary>
public class Commit
{
    public int Id { get; set; }

    /// <summary>
    /// Commit hash. Unique per repository rather than globally: the same commit
    /// legitimately appears in a fork, and forks are separate tracked repositories.
    /// </summary>
    public string Sha { get; set; } = string.Empty;

    public int RepositoryId { get; set; }
    public TrackedRepository? Repository { get; set; }

    /// <summary>
    /// Null when the author has no matching provider account — a local git
    /// config email the provider cannot resolve. The raw author fields below
    /// preserve attribution in that case.
    /// </summary>
    public int? ContributorId { get; set; }
    public Contributor? Contributor { get; set; }

    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorEmail { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Author date, i.e. when the work was written. Commit trends are bucketed
    /// on this rather than the committer date, which rebasing rewrites.
    /// </summary>
    public DateTime CommittedAt { get; set; }
}
