using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DevDynamics.API.SourceControl.GitHub;

/// <summary>
/// GitHub REST API implementation of <see cref="ISourceControlProvider"/>.
///
/// All GitHub-specific transport concerns are contained here — Link-header
/// pagination, ETag conditional requests, rate-limit headers, Retry-After and
/// backoff. The sync engine above this class is provider-agnostic.
/// </summary>
public class GitHubProvider : ISourceControlProvider
{
    private readonly HttpClient _http;
    private readonly GitHubOptions _options;
    private readonly ILogger<GitHubProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ProviderKey => "GitHub";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Token);

    public GitHubProvider(
        HttpClient http,
        IOptions<GitHubOptions> options,
        ILogger<GitHubProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _http = http;

        _http.BaseAddress = new Uri(_options.ApiBaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.Token);
        }
    }

    // =====================================================================
    // Repository metadata
    // =====================================================================

    public async Task<RepositoryDescriptor?> GetRepositoryAsync(
        string fullName,
        CancellationToken cancellationToken = default)
    {
        var (owner, name) = SplitFullName(fullName);

        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{name}"),
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new RepositoryDescriptor(
            ExternalId: GetString(root, "id") ?? string.Empty,
            Owner: root.GetProperty("owner").GetProperty("login").GetString() ?? owner,
            Name: root.GetProperty("name").GetString() ?? name,
            FullName: root.GetProperty("full_name").GetString() ?? fullName,
            Description: GetString(root, "description"),
            Language: GetString(root, "language"),
            HtmlUrl: GetString(root, "html_url"),
            DefaultBranch: GetString(root, "default_branch"),
            StarCount: GetInt(root, "stargazers_count") ?? 0,
            IsPrivate: root.TryGetProperty("private", out var p) && p.GetBoolean());
    }

    // =====================================================================
    // Commits
    // =====================================================================

    public async IAsyncEnumerable<CommitRecord> GetCommitsAsync(
        SyncContext context,
        SyncFetchResult result,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var since = context.Since?.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var path = $"repos/{context.Owner}/{context.Name}/commits?per_page={context.PageSize}";
        if (since is not null)
        {
            path += $"&since={since}";
        }

        await foreach (var element in EnumeratePagesAsync(
            path, context, result, isFirstResource: true, cancellationToken))
        {
            var commit = element.GetProperty("commit");
            var authorNode = commit.GetProperty("author");

            var committedAt = authorNode.TryGetProperty("date", out var dateProp)
                && dateProp.TryGetDateTime(out var parsed)
                    ? parsed.ToUniversalTime()
                    : DateTime.UtcNow;

            yield return new CommitRecord(
                Sha: GetString(element, "sha") ?? string.Empty,
                AuthorName: GetString(authorNode, "name") ?? "unknown",
                AuthorEmail: GetString(authorNode, "email"),
                Message: Truncate(GetString(commit, "message") ?? string.Empty, 1000),
                CommittedAt: committedAt,
                Author: ReadContributor(element, "author"));
        }
    }

    // =====================================================================
    // Pull requests
    // =====================================================================

    public async IAsyncEnumerable<PullRequestRecord> GetPullRequestsAsync(
        SyncContext context,
        SyncFetchResult result,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Sorted by update time, newest first. Pull requests mutate after
        // creation, so walking by update time is what allows an incremental
        // sync to notice that an older PR has since been merged.
        var path = $"repos/{context.Owner}/{context.Name}/pulls" +
                   $"?state=all&sort=updated&direction=desc&per_page={context.PageSize}";

        await foreach (var element in EnumeratePagesAsync(
            path, context, result, isFirstResource: false, cancellationToken))
        {
            var updatedAt = GetDate(element, "updated_at") ?? DateTime.UtcNow;

            // The feed is ordered by update time, so the first item older than
            // the cursor means everything after it is older too.
            if (context.Since.HasValue && updatedAt < context.Since.Value)
            {
                yield break;
            }

            yield return new PullRequestRecord(
                ExternalId: GetString(element, "id") ?? string.Empty,
                Number: GetInt(element, "number") ?? 0,
                Title: Truncate(GetString(element, "title") ?? string.Empty, 500),
                State: GetString(element, "state") ?? "unknown",
                CreatedAt: GetDate(element, "created_at") ?? updatedAt,
                UpdatedAt: updatedAt,
                MergedAt: GetDate(element, "merged_at"),
                ClosedAt: GetDate(element, "closed_at"),
                Author: ReadContributor(element, "user"));
        }
    }

    // =====================================================================
    // Pagination
    // =====================================================================

    /// <summary>
    /// Walks pages by following the Link header's rel="next", stopping at the
    /// configured page budget, when the rate-limit floor is reached, or when a
    /// conditional request reports nothing changed.
    /// </summary>
    private async IAsyncEnumerable<JsonElement> EnumeratePagesAsync(
        string path,
        SyncContext context,
        SyncFetchResult result,
        bool isFirstResource,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? nextUrl = path;
        var page = 0;

        while (nextUrl is not null && page < context.MaxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = nextUrl;
            var isFirstPage = page == 0;

            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Conditional request on the first page only: if it is
                // unchanged, nothing later in the sequence changed either.
                // A 304 does not count against the rate limit.
                if (isFirstPage && !string.IsNullOrEmpty(context.SyncToken))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", context.SyncToken);
                }

                return request;
            }, cancellationToken);

            if (isFirstPage)
            {
                result.NewSyncToken = response.Headers.ETag?.Tag;
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                result.NotModified = true;
                yield break;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                throw new RepositoryNotAccessibleException(
                    $"{context.Owner}/{context.Name} returned {(int)response.StatusCode}. " +
                    "It may have been deleted, renamed, or made private.");
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            var count = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                count++;
                yield return element.Clone();
            }

            page++;
            result.PagesFetched = page;

            if (count == 0)
            {
                yield break;
            }

            // Stop before exhausting the budget so interactive requests, such as
            // validating a newly added repository, still succeed.
            var remaining = ReadHeaderInt(response, "x-ratelimit-remaining");
            if (remaining is not null && remaining <= _options.RateLimitFloor)
            {
                var reset = ReadHeaderInt(response, "x-ratelimit-reset");
                result.RateLimitedUntilUtc = reset is not null
                    ? DateTimeOffset.FromUnixTimeSeconds(reset.Value).UtcDateTime
                    : DateTime.UtcNow.AddMinutes(5);

                result.Truncated = true;

                _logger.LogWarning(
                    "Rate limit floor reached ({Remaining} remaining); pausing sync until {Reset:u}.",
                    remaining, result.RateLimitedUntilUtc);

                yield break;
            }

            nextUrl = ParseNextLink(response);

            // Flag truncation here rather than after the loop: a consumer that
            // stops early (the pull request cutoff uses yield break) disposes
            // this enumerator mid-iteration, and any code after the loop would
            // never run — silently under-reporting truncation.
            if (nextUrl is not null && page >= context.MaxPages)
            {
                // More data exists but the page budget stopped us. Cursors still
                // advance, so the next run resumes instead of redoing this work.
                result.Truncated = true;
            }
        }
    }

    /// <summary>Extracts rel="next" from GitHub's Link header.</summary>
    private static string? ParseNextLink(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var segment in string.Join(',', values).Split(','))
        {
            var parts = segment.Split(';');
            if (parts.Length < 2) continue;

            if (!parts[1].Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase)) continue;

            return parts[0].Trim().Trim('<', '>');
        }

        return null;
    }

    // =====================================================================
    // Transport
    // =====================================================================

    /// <summary>
    /// Retries transient failures. Honours Retry-After for both primary and
    /// secondary rate limits, and backs off exponentially on 5xx. Does not
    /// retry 4xx responses, which will not succeed on a second attempt.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            response?.Dispose();

            using var request = requestFactory();
            response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                var delay = GetRetryAfterDelay(response);

                // A 403 with no Retry-After and rate limit intact is a genuine
                // permission problem; let the caller handle it.
                if (delay is null)
                {
                    return response;
                }

                if (attempt == _options.MaxRetries)
                {
                    return response;
                }

                _logger.LogWarning(
                    "Secondary rate limit hit; waiting {Delay}s before retry {Attempt}.",
                    delay.Value.TotalSeconds, attempt + 1);

                await Task.Delay(delay.Value, cancellationToken);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < _options.MaxRetries)
            {
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt))
                              + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));

                _logger.LogWarning(
                    "GitHub returned {Status}; retrying in {Backoff}s.",
                    (int)response.StatusCode, backoff.TotalSeconds);

                await Task.Delay(backoff, cancellationToken);
                continue;
            }

            return response;
        }

        return response!;
    }

    private static TimeSpan? GetRetryAfterDelay(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("retry-after", out var retryAfter)
            && int.TryParse(retryAfter.FirstOrDefault(), out var seconds))
        {
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 300));
        }

        // Primary rate limit exhaustion: remaining is 0 and reset says when.
        var remaining = ReadHeaderInt(response, "x-ratelimit-remaining");
        if (remaining is 0)
        {
            var reset = ReadHeaderInt(response, "x-ratelimit-reset");
            if (reset is not null)
            {
                var wait = DateTimeOffset.FromUnixTimeSeconds(reset.Value) - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero
                    ? TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds, 1, 300))
                    : TimeSpan.FromSeconds(1);
            }
        }

        return null;
    }

    private static int? ReadHeaderInt(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values)
        && int.TryParse(values.FirstOrDefault(), out var parsed)
            ? parsed
            : null;

    // =====================================================================
    // Parsing helpers
    // =====================================================================

    private static ContributorRecord? ReadContributor(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var user)
            || user.ValueKind != JsonValueKind.Object)
        {
            // Commits by an address GitHub cannot map to an account have a null
            // author. The raw git author fields preserve attribution.
            return null;
        }

        var login = GetString(user, "login");
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        return new ContributorRecord(
            ExternalId: GetString(user, "id") ?? login,
            Login: login,
            Name: null,
            AvatarUrl: GetString(user, "avatar_url"),
            HtmlUrl: GetString(user, "html_url"),
            IsBot: string.Equals(GetString(user, "type"), "Bot", StringComparison.OrdinalIgnoreCase)
                   || login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                _ => null
            }
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static DateTime? GetDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTime(out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static (string Owner, string Name) SplitFullName(string fullName)
    {
        var parts = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length != 2)
        {
            throw new ArgumentException(
                $"Repository must be in 'owner/name' form, but was '{fullName}'.", nameof(fullName));
        }

        return (parts[0], parts[1]);
    }
}
