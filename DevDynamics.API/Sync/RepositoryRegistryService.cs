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
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.FullName)
            .ToListAsync(cancellationToken);
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
