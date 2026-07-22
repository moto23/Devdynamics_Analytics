using DevDynamics.API.Data;
using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Sync;

public class ScheduleOptions
{
    public const string SectionName = "Schedule";

    /// <summary>Off by default: scheduled syncs spend API quota and database compute.</summary>
    public bool Enabled { get; set; }

    /// <summary>How often the scheduler looks for work.</summary>
    public int CheckIntervalMinutes { get; set; } = 15;

    /// <summary>Default staleness threshold, overridable per repository.</summary>
    public int DefaultSyncIntervalMinutes { get; set; } = 360;

    /// <summary>
    /// Cap on how many repositories are queued per tick, so a large registry
    /// cannot exhaust the API rate limit in one pass.
    /// </summary>
    public int MaxRepositoriesPerRun { get; set; } = 5;
}

/// <summary>
/// Queues repositories whose data has gone stale.
///
/// It only enqueues; the existing sync worker still does the work one
/// repository at a time, so scheduled and manual syncs share the same
/// serialisation, rate-limit handling and idempotency guarantees.
/// </summary>
public class ScheduledSyncWorker(
    ISyncQueue queue,
    IServiceScopeFactory scopeFactory,
    Microsoft.Extensions.Options.IOptions<ScheduleOptions> options,
    ILogger<ScheduledSyncWorker> logger) : BackgroundService
{
    private readonly ScheduleOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Scheduled sync is disabled.");
            return;
        }

        logger.LogInformation(
            "Scheduled sync enabled: checking every {Interval} minutes, default staleness {Stale} minutes.",
            _options.CheckIntervalMinutes, _options.DefaultSyncIntervalMinutes);

        // Let startup settle before the first pass.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await QueueStaleRepositoriesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // A bad pass must never take the scheduler down.
                logger.LogError(ex, "Scheduled sync pass failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, _options.CheckIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task QueueStaleRepositoriesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var defaultThreshold = now.AddMinutes(-Math.Max(1, _options.DefaultSyncIntervalMinutes));

        // Candidates are filtered in SQL; the per-repository override is applied
        // afterwards over a small set rather than as a correlated predicate.
        var candidates = await context.TrackedRepositories
            .AsNoTracking()
            .Where(r => r.IsActive
                        && r.SyncStatus != SyncStatuses.Syncing
                        && r.SyncStatus != SyncStatuses.Queued
                        && (r.LastSyncCompletedAtUtc == null || r.LastSyncCompletedAtUtc < defaultThreshold
                            || r.SyncIntervalMinutes != null))
            .OrderBy(r => r.LastSyncCompletedAtUtc)
            .Select(r => new { r.Id, r.FullName, r.LastSyncCompletedAtUtc, r.SyncIntervalMinutes })
            .Take(_options.MaxRepositoriesPerRun * 4)
            .ToListAsync(cancellationToken);

        var due = candidates
            .Where(r =>
            {
                // A non-positive override opts the repository out of scheduling
                // without disabling manual syncs.
                var interval = r.SyncIntervalMinutes ?? _options.DefaultSyncIntervalMinutes;
                if (interval <= 0) return false;

                return r.LastSyncCompletedAtUtc is null
                       || r.LastSyncCompletedAtUtc < now.AddMinutes(-interval);
            })
            .Take(_options.MaxRepositoriesPerRun)
            .ToList();

        if (due.Count == 0) return;

        foreach (var repository in due)
        {
            if (queue.Enqueue(repository.Id))
            {
                logger.LogInformation("Scheduled sync queued for {Repo}.", repository.FullName);
            }
        }
    }
}
