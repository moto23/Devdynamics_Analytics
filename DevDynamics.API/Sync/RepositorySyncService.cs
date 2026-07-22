using System.Diagnostics;
using DevDynamics.API.Data;
using DevDynamics.API.Models;
using DevDynamics.API.SourceControl;
using DevDynamics.API.SourceControl.GitHub;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevDynamics.API.Sync;

public class SyncOutcome
{
    public bool Success { get; set; }
    public int CommitsIngested { get; set; }
    public int PullRequestsIngested { get; set; }
    public int ContributorsSeen { get; set; }
    public bool Truncated { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>
/// Synchronises one tracked repository.
///
/// Entirely provider-agnostic: it resolves a provider by the repository's
/// Provider key and consumes neutral records. Adding another Git host requires
/// no change here.
///
/// Idempotency comes from matching on natural keys before insert, backed by
/// unique constraints in the database. Re-running a sync over an overlapping
/// window updates rows rather than duplicating them.
/// </summary>
public class RepositorySyncService(
    AppDbContext context,
    ISourceControlProviderRegistry registry,
    IOptions<GitHubOptions> options,
    ILogger<RepositorySyncService> logger)
{
    private readonly GitHubOptions _options = options.Value;

    public async Task<SyncOutcome> SyncAsync(
        int repositoryId,
        string trigger = SyncTriggers.Manual,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = new SyncOutcome();

        // History row: the repository holds only the latest outcome, so without
        // this a run that failed and was retried leaves no trace.
        var run = new SyncRun
        {
            RepositoryId = repositoryId,
            StartedAtUtc = DateTime.UtcNow,
            Status = SyncStatuses.Syncing,
            Trigger = trigger
        };

        var repository = await context.TrackedRepositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

        if (repository is null)
        {
            outcome.Error = $"Repository {repositoryId} not found.";
            return outcome;
        }

        var provider = registry.Resolve(repository.Provider);

        if (provider is null || !provider.IsConfigured)
        {
            outcome.Error = $"Provider '{repository.Provider}' is unavailable or not configured.";
            await MarkFailedAsync(repository, outcome.Error, stopwatch, cancellationToken, run, outcome);
            return outcome;
        }

        repository.SyncStatus = SyncStatuses.Syncing;
        repository.LastSyncStartedAtUtc = DateTime.UtcNow;
        repository.LastSyncError = null;
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await RefreshMetadataAsync(repository, provider, cancellationToken);

            await RefreshLanguagesAsync(repository, provider, cancellationToken);

            var commitResult = await SyncCommitsAsync(repository, provider, outcome, cancellationToken);
            var pullResult = await SyncPullRequestsAsync(repository, provider, outcome, cancellationToken);

            await RefreshCountersAsync(repository, cancellationToken);

            outcome.Truncated = commitResult.Truncated || pullResult.Truncated;
            outcome.Success = true;

            repository.SyncStatus = outcome.Truncated
                ? SyncStatuses.PartiallySynced
                : SyncStatuses.Succeeded;

            repository.LastSyncCompletedAtUtc = DateTime.UtcNow;
            repository.LastSyncDurationMs = (int)stopwatch.ElapsedMilliseconds;

            RecordRun(run, repository.SyncStatus, outcome, stopwatch, null);

            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Synced {Repo}: {Commits} commits, {Pulls} pull requests, truncated={Truncated}, {Elapsed}ms.",
                repository.FullName, outcome.CommitsIngested, outcome.PullRequestsIngested,
                outcome.Truncated, stopwatch.ElapsedMilliseconds);
        }
        catch (RepositoryNotAccessibleException ex)
        {
            // Terminal: deleted, renamed or made private. Retrying cannot help,
            // so deactivate rather than fail on every future run.
            repository.IsActive = false;
            outcome.Error = ex.Message;
            await MarkFailedAsync(repository, ex.Message, stopwatch, cancellationToken, run, outcome);

            logger.LogWarning("Deactivated {Repo}: {Message}", repository.FullName, ex.Message);
        }
        catch (Exception ex)
        {
            outcome.Error = ex.Message;
            await MarkFailedAsync(repository, ex.Message, stopwatch, cancellationToken, run, outcome);

            logger.LogError(ex, "Sync failed for {Repo}.", repository.FullName);
        }

        outcome.DurationMs = stopwatch.ElapsedMilliseconds;
        return outcome;
    }

    // =====================================================================
    // Metadata
    // =====================================================================

    private async Task RefreshMetadataAsync(
        TrackedRepository repository,
        ISourceControlProvider provider,
        CancellationToken cancellationToken)
    {
        var descriptor = await provider.GetRepositoryAsync(repository.FullName, cancellationToken)
            ?? throw new RepositoryNotAccessibleException(
                $"{repository.FullName} is not accessible.");

        repository.ExternalId = descriptor.ExternalId;
        repository.Owner = descriptor.Owner;
        repository.Name = descriptor.Name;
        repository.FullName = descriptor.FullName;
        repository.Description = descriptor.Description;
        repository.Language = descriptor.Language;
        repository.HtmlUrl = descriptor.HtmlUrl;
        repository.DefaultBranch = descriptor.DefaultBranch;
        repository.StarCount = descriptor.StarCount;
        repository.ForkCount = descriptor.ForkCount;
        repository.OpenIssueCount = descriptor.OpenIssueCount;
        repository.IsPrivate = descriptor.IsPrivate;
    }

    /// <summary>
    /// Refreshes the per-language byte breakdown. One extra request per sync,
    /// which is cheap; the primary language alone is not a distribution.
    /// </summary>
    private async Task RefreshLanguagesAsync(
        TrackedRepository repository,
        ISourceControlProvider provider,
        CancellationToken cancellationToken)
    {
        var languages = await provider.GetLanguagesAsync(repository.FullName, cancellationToken);
        if (languages.Count == 0) return;

        var existing = await context.RepositoryLanguages
            .Where(l => l.RepositoryId == repository.Id)
            .ToListAsync(cancellationToken);

        foreach (var (language, bytes) in languages)
        {
            var row = existing.FirstOrDefault(l => l.Language == language);

            if (row is null)
            {
                context.RepositoryLanguages.Add(new RepositoryLanguage
                {
                    RepositoryId = repository.Id,
                    Language = language,
                    Bytes = bytes
                });
            }
            else
            {
                row.Bytes = bytes;
            }
        }

        // Languages removed upstream should disappear rather than linger.
        foreach (var stale in existing.Where(l => !languages.ContainsKey(l.Language)))
        {
            context.RepositoryLanguages.Remove(stale);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    // =====================================================================
    // Commits
    // =====================================================================

    private async Task<SyncFetchResult> SyncCommitsAsync(
        TrackedRepository repository,
        ISourceControlProvider provider,
        SyncOutcome outcome,
        CancellationToken cancellationToken)
    {
        var result = new SyncFetchResult();

        var since = repository.CommitsSyncedThroughUtc.HasValue
            ? repository.CommitsSyncedThroughUtc.Value.AddMinutes(-_options.OverlapBufferMinutes)
            : DateTime.UtcNow.AddDays(-_options.SyncWindowDays);

        var syncContext = new SyncContext
        {
            Owner = repository.Owner,
            Name = repository.Name,
            Since = since,
            SyncToken = repository.CommitsSyncToken,
            PageSize = _options.PageSize,
            MaxPages = _options.MaxPagesPerResource
        };

        // Existing SHAs in the affected window, so re-fetched overlap does not
        // attempt duplicate inserts.
        var existingShas = (await context.Commits
            .Where(c => c.RepositoryId == repository.Id && c.CommittedAt >= since)
            .Select(c => c.Sha)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var newest = repository.CommitsSyncedThroughUtc;
        var resolver = new ContributorResolver(context, repository.Provider);

        // Streamed in bounded batches: memory stays flat regardless of how large
        // the repository is, and each batch costs a fixed number of round trips
        // rather than one per row.
        var batch = new List<CommitRecord>(BatchSize);

        await foreach (var record in provider.GetCommitsAsync(syncContext, result, cancellationToken))
        {
            if (newest is null || record.CommittedAt > newest)
            {
                newest = record.CommittedAt;
            }

            // Skips both rows already stored and duplicates within this run.
            if (string.IsNullOrWhiteSpace(record.Sha) || !existingShas.Add(record.Sha))
            {
                continue;
            }

            batch.Add(record);

            if (batch.Count >= BatchSize)
            {
                outcome.CommitsIngested += await FlushCommitsAsync(
                    repository, batch, resolver, cancellationToken);

                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            outcome.CommitsIngested += await FlushCommitsAsync(
                repository, batch, resolver, cancellationToken);
        }

        outcome.ContributorsSeen += resolver.NewContributorCount;

        // Advance only on success, so an interrupted run resumes rather than
        // skipping the window it failed on.
        if (newest.HasValue)
        {
            repository.CommitsSyncedThroughUtc = newest;
        }

        if (result.NewSyncToken is not null)
        {
            repository.CommitsSyncToken = result.NewSyncToken;
        }

        return result;
    }

    // =====================================================================
    // Pull requests
    // =====================================================================

    private async Task<SyncFetchResult> SyncPullRequestsAsync(
        TrackedRepository repository,
        ISourceControlProvider provider,
        SyncOutcome outcome,
        CancellationToken cancellationToken)
    {
        var result = new SyncFetchResult();

        var since = repository.PullsSyncedThroughUtc.HasValue
            ? repository.PullsSyncedThroughUtc.Value.AddMinutes(-_options.OverlapBufferMinutes)
            : DateTime.UtcNow.AddDays(-_options.SyncWindowDays);

        var syncContext = new SyncContext
        {
            Owner = repository.Owner,
            Name = repository.Name,
            Since = since,
            SyncToken = repository.PullsSyncToken,
            PageSize = _options.PageSize,
            MaxPages = _options.MaxPagesPerResource
        };

        var newest = repository.PullsSyncedThroughUtc;
        var resolver = new ContributorResolver(context, repository.Provider);
        var batch = new List<PullRequestRecord>(BatchSize);

        await foreach (var record in provider.GetPullRequestsAsync(syncContext, result, cancellationToken))
        {
            if (newest is null || record.UpdatedAt > newest)
            {
                newest = record.UpdatedAt;
            }

            batch.Add(record);

            if (batch.Count >= BatchSize)
            {
                outcome.PullRequestsIngested += await FlushPullRequestsAsync(
                    repository, batch, resolver, cancellationToken);

                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            outcome.PullRequestsIngested += await FlushPullRequestsAsync(
                repository, batch, resolver, cancellationToken);
        }

        outcome.ContributorsSeen += resolver.NewContributorCount;

        if (newest.HasValue)
        {
            repository.PullsSyncedThroughUtc = newest;
        }

        if (result.NewSyncToken is not null)
        {
            repository.PullsSyncToken = result.NewSyncToken;
        }

        return result;
    }

    // =====================================================================
    // Contributors
    // =====================================================================

    /// <summary>Rows per database round trip; see GitHubOptions.WriteBatchSize.</summary>
    private int BatchSize => Math.Max(1, _options.WriteBatchSize);

    /// <summary>Matches the LastSyncError column length.</summary>
    private const int MaxErrorLength = 1000;

    /// <summary>
    /// Persists a batch of commits: one query to resolve contributors, one
    /// insert for any new ones, one insert for the commits.
    /// </summary>
    private async Task<int> FlushCommitsAsync(
        TrackedRepository repository,
        List<CommitRecord> batch,
        ContributorResolver resolver,
        CancellationToken cancellationToken)
    {
        await resolver.ResolveAsync(batch.Select(r => r.Author), cancellationToken);

        var commits = batch.Select(record => new Commit
        {
            RepositoryId = repository.Id,
            Sha = record.Sha,
            AuthorName = record.AuthorName,
            AuthorEmail = record.AuthorEmail,
            Message = record.Message,
            CommittedAt = record.CommittedAt,
            ContributorId = resolver.IdFor(record.Author)
        }).ToList();

        context.Commits.AddRange(commits);
        await context.SaveChangesAsync(cancellationToken);

        return commits.Count;
    }

    /// <summary>
    /// Upserts a batch of pull requests. Existing rows for the batch's numbers
    /// are loaded in a single query, so a previously open pull request picks up
    /// its merge timestamp without a per-row lookup.
    /// </summary>
    private async Task<int> FlushPullRequestsAsync(
        TrackedRepository repository,
        List<PullRequestRecord> batch,
        ContributorResolver resolver,
        CancellationToken cancellationToken)
    {
        await resolver.ResolveAsync(batch.Select(r => r.Author), cancellationToken);

        var numbers = batch.Select(r => r.Number).ToList();

        // Tracked (not AsNoTracking): these entities are updated in place.
        var existing = await context.PullRequests
            .Where(p => p.RepositoryId == repository.Id && numbers.Contains(p.Number))
            .ToDictionaryAsync(p => p.Number, cancellationToken);

        var inserted = 0;
        var newRows = new List<PullRequest>();

        foreach (var record in batch)
        {
            if (existing.TryGetValue(record.Number, out var current))
            {
                current.Title = record.Title;
                current.State = record.State;
                current.UpdatedAt = record.UpdatedAt;
                current.MergedAt = record.MergedAt;
                current.ClosedAt = record.ClosedAt;
                current.ContributorId ??= resolver.IdFor(record.Author);
                current.AuthorLogin ??= record.Author?.Login;
                continue;
            }

            newRows.Add(new PullRequest
            {
                RepositoryId = repository.Id,
                ExternalId = record.ExternalId,
                Number = record.Number,
                Title = record.Title,
                State = record.State,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
                MergedAt = record.MergedAt,
                ClosedAt = record.ClosedAt,
                ContributorId = resolver.IdFor(record.Author),
                AuthorLogin = record.Author?.Login
            });

            inserted++;
        }

        if (newRows.Count > 0)
        {
            context.PullRequests.AddRange(newRows);
        }

        await context.SaveChangesAsync(cancellationToken);

        return inserted;
    }

    // =====================================================================
    // Counters
    // =====================================================================

    private async Task RefreshCountersAsync(TrackedRepository repository, CancellationToken cancellationToken)
    {
        repository.TotalCommits = await context.Commits
            .CountAsync(c => c.RepositoryId == repository.Id, cancellationToken);

        repository.TotalPullRequests = await context.PullRequests
            .CountAsync(p => p.RepositoryId == repository.Id, cancellationToken);

        repository.TotalContributors = await context.Commits
            .Where(c => c.RepositoryId == repository.Id && c.ContributorId != null)
            .Select(c => c.ContributorId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        TrackedRepository repository,
        string error,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        SyncRun? run = null,
        SyncOutcome? outcome = null)
    {
        repository.SyncStatus = SyncStatuses.Failed;
        repository.LastSyncError = Truncate(error);
        repository.LastSyncCompletedAtUtc = DateTime.UtcNow;
        repository.LastSyncDurationMs = (int)stopwatch.ElapsedMilliseconds;

        if (run is not null)
        {
            RecordRun(run, SyncStatuses.Failed, outcome ?? new SyncOutcome(), stopwatch, error);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Completes the history row and attaches it for saving.</summary>
    private void RecordRun(
        SyncRun run,
        string status,
        SyncOutcome outcome,
        Stopwatch stopwatch,
        string? error)
    {
        run.Status = status;
        run.CompletedAtUtc = DateTime.UtcNow;
        run.DurationMs = (int)stopwatch.ElapsedMilliseconds;
        run.CommitsIngested = outcome.CommitsIngested;
        run.PullRequestsIngested = outcome.PullRequestsIngested;
        run.ContributorsAdded = outcome.ContributorsSeen;
        run.Truncated = outcome.Truncated;
        run.Error = error is null ? null : Truncate(error);

        context.SyncRuns.Add(run);
    }

    private static string Truncate(string value) =>
        value.Length > MaxErrorLength ? value[..MaxErrorLength] : value;
}
