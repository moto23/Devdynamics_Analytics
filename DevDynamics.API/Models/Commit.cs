namespace DevDynamics.API.Models;

/// <summary>
/// A single commit on a tracked repository's default branch.
/// </summary>
public class Commit
{
    public int Id { get; set; }

    /// <summary>Full commit SHA; unique across the dataset.</summary>
    public string Sha { get; set; } = string.Empty;

    public int RepositoryId { get; set; }
    public Repository? Repository { get; set; }

    /// <summary>
    /// Null when the commit author has no matching GitHub account
    /// (e.g. a local git config email that GitHub cannot resolve).
    /// </summary>
    public int? ContributorId { get; set; }
    public Contributor? Contributor { get; set; }

    /// <summary>Raw git author details, kept for commits with no GitHub user.</summary>
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorEmail { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CommittedAt { get; set; }
}
