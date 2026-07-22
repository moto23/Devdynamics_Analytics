namespace DevDynamics.API.SourceControl;

/// <summary>
/// Provider-neutral records exchanged between a source control provider and the
/// sync engine. Nothing here is GitHub-specific: adding GitLab or Azure DevOps
/// means implementing ISourceControlProvider to emit these same shapes, with no
/// change to the sync engine, the schema, or analytics.
/// </summary>
public record RepositoryDescriptor(
    string ExternalId,
    string Owner,
    string Name,
    string FullName,
    string? Description,
    string? Language,
    string? HtmlUrl,
    string? DefaultBranch,
    int StarCount,
    int ForkCount,
    int OpenIssueCount,
    bool IsPrivate);

public record ContributorRecord(
    string ExternalId,
    string Login,
    string? Name,
    string? AvatarUrl,
    string? HtmlUrl,
    bool IsBot);

public record CommitRecord(
    string Sha,
    string AuthorName,
    string? AuthorEmail,
    string Message,
    DateTime CommittedAt,
    ContributorRecord? Author);

public record PullRequestRecord(
    string ExternalId,
    int Number,
    string Title,
    string State,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? MergedAt,
    DateTime? ClosedAt,
    ContributorRecord? Author);

/// <summary>
/// Everything a provider needs to fetch one repository incrementally, plus the
/// caller's bounds. Cursors are opaque to the sync engine and interpreted only
/// by the provider that issued them.
/// </summary>
public class SyncContext
{
    public required string Owner { get; init; }
    public required string Name { get; init; }

    /// <summary>Only fetch items changed after this point, when known.</summary>
    public DateTime? Since { get; init; }

    /// <summary>Provider change token from the previous sync (an ETag for GitHub).</summary>
    public string? SyncToken { get; init; }

    public int PageSize { get; init; } = 100;
    public int MaxPages { get; init; } = 20;
}

/// <summary>
/// Outcome of a paged fetch. Reports why the fetch stopped so the caller can
/// distinguish "finished" from "ran out of budget", and record the difference.
/// </summary>
public class SyncFetchResult
{
    public string? NewSyncToken { get; set; }

    /// <summary>True when the provider confirmed nothing changed (HTTP 304).</summary>
    public bool NotModified { get; set; }

    /// <summary>True when the page or rate-limit budget stopped the fetch early.</summary>
    public bool Truncated { get; set; }

    public int PagesFetched { get; set; }

    /// <summary>Provider requests that syncing pause until this time.</summary>
    public DateTime? RateLimitedUntilUtc { get; set; }
}

/// <summary>
/// Raised when a repository cannot be found or is not accessible with the
/// configured credentials. Treated as terminal: retrying will not help.
/// </summary>
public class RepositoryNotAccessibleException(string message) : Exception(message);

/// <summary>
/// A source of repository, commit and pull request data.
///
/// Implementations own their transport concerns entirely — pagination,
/// conditional requests, retries and rate limiting — because those differ
/// per provider. The sync engine only consumes the neutral records above.
/// </summary>
public interface ISourceControlProvider
{
    /// <summary>Matches TrackedRepository.Provider, e.g. "GitHub".</summary>
    string ProviderKey { get; }

    /// <summary>True when credentials are configured and the provider is usable.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Resolves "owner/name". Returns null when it does not exist or is not
    /// visible, so callers can reject a bad repository before persisting it.
    /// </summary>
    Task<RepositoryDescriptor?> GetRepositoryAsync(
        string fullName,
        CancellationToken cancellationToken = default);

    /// <summary>Streams commits newest-first, honouring the context's bounds.</summary>
    IAsyncEnumerable<CommitRecord> GetCommitsAsync(
        SyncContext context,
        SyncFetchResult result,
        CancellationToken cancellationToken = default);

    /// <summary>Streams pull requests ordered by most recently updated.</summary>
    IAsyncEnumerable<PullRequestRecord> GetPullRequestsAsync(
        SyncContext context,
        SyncFetchResult result,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bytes of source per language. Returns an empty map when the provider
    /// cannot supply one, so callers never need to special-case it.
    /// </summary>
    Task<IReadOnlyDictionary<string, long>> GetLanguagesAsync(
        string fullName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the provider for a tracked repository. Registering an additional
/// provider is the only code change a new Git host requires.
/// </summary>
public interface ISourceControlProviderRegistry
{
    ISourceControlProvider? Resolve(string providerKey);
    IReadOnlyCollection<string> AvailableProviders { get; }
}

public class SourceControlProviderRegistry(IEnumerable<ISourceControlProvider> providers)
    : ISourceControlProviderRegistry
{
    private readonly Dictionary<string, ISourceControlProvider> _providers =
        providers.ToDictionary(p => p.ProviderKey, StringComparer.OrdinalIgnoreCase);

    public ISourceControlProvider? Resolve(string providerKey) =>
        _providers.TryGetValue(providerKey, out var provider) ? provider : null;

    public IReadOnlyCollection<string> AvailableProviders => _providers.Keys;
}
