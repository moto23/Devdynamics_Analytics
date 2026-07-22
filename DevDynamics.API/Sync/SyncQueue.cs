using System.Threading.Channels;
using DevDynamics.API.Data;
using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Sync;

/// <summary>
/// Work queue for repository syncs.
///
/// In-memory by design for the current single-instance deployment. Sync state
/// lives in the database, not the queue, so a lost queue entry costs a re-trigger
/// rather than data: cursors have not advanced, and upserts are idempotent.
/// Swapping this for a durable queue later means replacing this class only.
/// </summary>
public interface ISyncQueue
{
    bool Enqueue(int repositoryId);
    IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken);
    int PendingCount { get; }
}

public class SyncQueue : ISyncQueue
{
    private readonly Channel<int> _channel =
        Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });

    // Prevents the same repository being queued repeatedly by impatient clicks.
    private readonly HashSet<int> _queued = [];
    private readonly object _gate = new();

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _queued.Count;
            }
        }
    }

    public bool Enqueue(int repositoryId)
    {
        lock (_gate)
        {
            if (!_queued.Add(repositoryId))
            {
                return false;
            }
        }

        if (_channel.Writer.TryWrite(repositoryId))
        {
            return true;
        }

        lock (_gate)
        {
            _queued.Remove(repositoryId);
        }

        return false;
    }

    public async IAsyncEnumerable<int> DequeueAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            lock (_gate)
            {
                _queued.Remove(id);
            }

            yield return id;
        }
    }
}

/// <summary>
/// Drains the sync queue one repository at a time.
///
/// Sequential on purpose: concurrent syncs would multiply pressure on a shared
/// API rate limit and on a free-tier database, for no throughput gain at this
/// scale. Raising concurrency later is a local change here.
/// </summary>
public class SyncWorker(
    ISyncQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Repository sync worker started.");

        // Recover repositories left mid-sync by a restart. Render's free tier
        // spins down when idle, so this is routine rather than exceptional.
        await RequeueInterruptedAsync(stoppingToken);

        await foreach (var repositoryId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<RepositorySyncService>();

                await syncService.SyncAsync(repositoryId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One repository must never take the worker down.
                logger.LogError(ex, "Unhandled error syncing repository {Id}.", repositoryId);
            }
        }

        logger.LogInformation("Repository sync worker stopped.");
    }

    private async Task RequeueInterruptedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var interrupted = await context.TrackedRepositories
                .Where(r => r.IsActive &&
                            (r.SyncStatus == SyncStatuses.Syncing || r.SyncStatus == SyncStatuses.Queued))
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in interrupted)
            {
                queue.Enqueue(id);
            }

            if (interrupted.Count > 0)
            {
                logger.LogInformation("Requeued {Count} interrupted sync(s).", interrupted.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not requeue interrupted syncs.");
        }
    }
}
