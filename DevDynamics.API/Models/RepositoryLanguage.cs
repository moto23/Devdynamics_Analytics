namespace DevDynamics.API.Models;

/// <summary>
/// Bytes of source per language in a repository.
///
/// TrackedRepository.Language holds only the single primary language the
/// provider reports, which is not a distribution. This is the real breakdown,
/// refreshed once per sync at a cost of one extra API request per repository.
/// </summary>
public class RepositoryLanguage
{
    public int Id { get; set; }

    public int RepositoryId { get; set; }
    public TrackedRepository? Repository { get; set; }

    public string Language { get; set; } = string.Empty;

    /// <summary>Bytes of code, as reported by the provider.</summary>
    public long Bytes { get; set; }
}
