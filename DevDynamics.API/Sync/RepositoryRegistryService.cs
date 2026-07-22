using DevDynamics.API.Analytics;
using DevDynamics.API.Data;
using DevDynamics.API.Models;
using DevDynamics.API.SourceControl;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Sync;

public class RegistryActionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public TrackedRepository? Repository { get; set; }
}

/// <summary>
/// Runtime management of the repository registry.
///
/// Every operation is a database change: adding, removing, enabling or
/// disabling a repository never requires a code change or redeploy, and the
/// platform scales from one repository to hundreds without modification.
/// </summary>
public class RepositoryRegistryService(
    AppDbContext context,
    ISourceControlProviderRegistry providers,
    ISyncQueue queue,
    ILogger<RepositoryRegistryService> logger)
{
    public async Task<List<TrackedRepository>> GetAllAsync(
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = context.TrackedRepositories.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query
            .OrderByDescending(r => r.IsPinned)
            .ThenByDescending(r => r.IsActive)
            .ThenBy(r => r.FullName)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Paginated, searchable, sortable registry listing.
    ///
    /// Sorting and searching run in SQL rather than over a materialised list,
    /// so the page stays cheap as the registry grows.
    /// </summary>
    public async Task<Connection<TrackedRepository>> GetPageAsync(
        bool includeInactive,
        string? search,
        string? sortBy,
        bool descending,
        string? after,
        int? first,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Cursor.PageSize(first, 25);
        var offset = Cursor.DecodeOffset(after);

        var query = context.TrackedRepositories.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r =>
                r.FullName.Contains(term) ||
                (r.Language != null && r.Language.Contains(term)) ||
                (r.Nickname != null && r.Nickname.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        query = (sortBy?.ToLowerInvariant()) switch
        {
            "commits"      => Order(query, r => r.TotalCommits, descending),
            "pullrequests" => Order(query, r => r.TotalPullRequests, descending),
            "contributors" => Order(query, r => r.TotalContributors, descending),
            "stars"        => Order(query, r => r.StarCount, descending),
            "lastsynced"   => descending
                ? query.OrderByDescending(r => r.LastSyncCompletedAtUtc).ThenBy(r => r.Id)
                : query.OrderBy(r => r.LastSyncCompletedAtUtc).ThenBy(r => r.Id),
            _ => descending
                ? query.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.FullName)
                : query.OrderByDescending(r => r.IsPinned).ThenBy(r => r.FullName)
        };

        var items = await query.Skip(offset).Take(pageSize).ToListAsync(cancellationToken);

        return new Connection<TrackedRepository>
        {
            Items = items,
            PageInfo = new PageInfo
            {
                TotalCount = totalCount,
                HasNextPage = offset + items.Count < totalCount,
                HasPreviousPage = offset > 0,
                EndCursor = Cursor.EncodeOffset(offset + items.Count)
            }
        };
    }

    private static IQueryable<TrackedRepository> Order<TKey>(
        IQueryable<TrackedRepository> query,
        System.Linq.Expressions.Expression<Func<TrackedRepository, TKey>> key,
        bool descending) =>
        descending
            ? query.OrderByDescending(key).ThenBy(r => r.Id)
            : query.OrderBy(key).ThenBy(r => r.Id);

    /// <summary>Updates user-managed metadata. Ingested fields are never touched here.</summary>
    public async Task<RegistryActionResult> UpdateAsync(
        int id,
        string? nickname,
        string? notes,
        bool? isPinned,
        int? syncIntervalMinutes,
        CancellationToken cancellationToken = default)
    {
        var repository = await context.TrackedRepositories
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (repository is null)
        {
            return Fail($"Repository {id} not found.");
        }

        // An empty string clears the field; null leaves it unchanged.
        if (nickname is not null)
            repository.Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();

        if (notes is not null)
            repository.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        if (isPinned.HasValue) repository.IsPinned = isPinned.Value;
        if (syncIntervalMinutes.HasValue) repository.SyncIntervalMinutes = syncIntervalMinutes.Value;

        await context.SaveChangesAsync(cancellationToken);

        return new RegistryActionResult
        {
            Success = true,
            Message = $"{repository.FullName} updated.",
            Repository = repository
        };
    }

    /// <summary>
    /// Adds any repository the configured provider can see. Validated against
    /// the provider before insert so a typo fails immediately with a clear
    /// message rather than as a background sync failure later.
    /// </summary>
    public async Task<RegistryActionResult> AddAsync(
        string fullName,
        string providerKey = "GitHub",
        string? addedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullName) || !fullName.Contains('/'))
        {
            return Fail("Repository must be in 'owner/name' form, for example 'dotnet/efcore'.");
        }

        fullName = fullName.Trim().TrimEnd('/');

        var provider = providers.Resolve(providerKey);

        if (provider is null)
        {
            return Fail($"Unknown provider '{providerKey}'. Available: {string.Join(", ", providers.AvailableProviders)}.");
        }

        if (!provider.IsConfigured)
        {
            return Fail($"Provider '{providerKey}' has no credentials configured.");
        }

        var existing = await context.TrackedRepositories.FirstOrDefaultAsync(
            r => r.Provider == providerKey && r.FullName == fullName, cancellationToken);

        if (existing is not null)
        {
            // Re-adding a previously removed-then-disabled repository reactivates
            // it rather than erroring, which is what a user actually wants.
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await context.SaveChangesAsync(cancellationToken);
                queue.Enqueue(existing.Id);

                return new RegistryActionResult
                {
                    Success = true,
                    Message = $"{fullName} was already tracked but inactive; reactivated and queued for sync.",
                    Repository = existing
                };
            }

            return Fail($"{fullName} is already tracked.");
        }

        var descriptor = await provider.GetRepositoryAsync(fullName, cancellationToken);

        if (descriptor is null)
        {
            return Fail($"{fullName} was not found or is not publicly accessible.");
        }

        var repository = new TrackedRepository
        {
            Provider = providerKey,
            ExternalId = descriptor.ExternalId,
            Owner = descriptor.Owner,
            Name = descriptor.Name,
            FullName = descriptor.FullName,
            Description = descriptor.Description,
            Language = descriptor.Language,
            HtmlUrl = descriptor.HtmlUrl,
            DefaultBranch = descriptor.DefaultBranch,
            StarCount = descriptor.StarCount,
            IsPrivate = descriptor.IsPrivate,
            IsActive = true,
            IsDemoData = false,
            AddedAtUtc = DateTime.UtcNow,
            AddedBy = addedBy,
            SyncStatus = SyncStatuses.Queued
        };

        context.TrackedRepositories.Add(repository);
        await context.SaveChangesAsync(cancellationToken);

        queue.Enqueue(repository.Id);

        logger.LogInformation("Added {Repo} to the registry.", repository.FullName);

        return new RegistryActionResult
        {
            Success = true,
            Message = $"{descriptor.FullName} added and queued for sync.",
            Repository = repository
        };
    }

    /// <summary>
    /// Removes a repository and, by cascade, its ingested commits and pull
    /// requests. Demo seed rows are ordinary rows and delete the same way.
    /// </summary>
    public async Task<RegistryActionResult> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        var repository = await context.TrackedRepositories
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (repository is null)
        {
            return Fail($"Repository {id} not found.");
        }

        context.TrackedRepositories.Remove(repository);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Removed {Repo} from the registry.", repository.FullName);

        return new RegistryActionResult
        {
            Success = true,
            Message = $"{repository.FullName} removed along with its ingested data."
        };
    }

    /// <summary>
    /// Enables or disables without discarding history. Disabled repositories are
    /// skipped by the sync worker and excluded from analytics.
    /// </summary>
    public async Task<RegistryActionResult> SetActiveAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var repository = await context.TrackedRepositories
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (repository is null)
        {
            return Fail($"Repository {id} not found.");
        }

        repository.IsActive = isActive;
        await context.SaveChangesAsync(cancellationToken);

        if (isActive)
        {
            queue.Enqueue(repository.Id);
        }

        return new RegistryActionResult
        {
            Success = true,
            Message = $"{repository.FullName} {(isActive ? "enabled and queued for sync" : "disabled")}.",
            Repository = repository
        };
    }

    public async Task<RegistryActionResult> QueueSyncAsync(int id, CancellationToken cancellationToken = default)
    {
        var repository = await context.TrackedRepositories
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (repository is null)
        {
            return Fail($"Repository {id} not found.");
        }

        if (!repository.IsActive)
        {
            return Fail($"{repository.FullName} is disabled. Enable it before syncing.");
        }

        repository.SyncStatus = SyncStatuses.Queued;
        await context.SaveChangesAsync(cancellationToken);

        queue.Enqueue(repository.Id);

        return new RegistryActionResult
        {
            Success = true,
            Message = $"{repository.FullName} queued for sync.",
            Repository = repository
        };
    }

    /// <summary>Queues every active repository, whatever the current count.</summary>
    public async Task<RegistryActionResult> QueueSyncAllAsync(CancellationToken cancellationToken = default)
    {
        var ids = await context.TrackedRepositories
            .Where(r => r.IsActive)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return Fail("No active repositories to sync.");
        }

        await context.TrackedRepositories
            .Where(r => r.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SyncStatus, SyncStatuses.Queued),
                cancellationToken);

        var queued = ids.Count(queue.Enqueue);

        return new RegistryActionResult
        {
            Success = true,
            Message = $"Queued {queued} of {ids.Count} active repositories for sync."
        };
    }

    private static RegistryActionResult Fail(string message) =>
        new() { Success = false, Message = message };
}
