using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TrackedRepository> TrackedRepositories => Set<TrackedRepository>();
    public DbSet<Contributor> Contributors => Set<Contributor>();
    public DbSet<Commit> Commits => Set<Commit>();
    public DbSet<PullRequest> PullRequests => Set<PullRequest>();
    public DbSet<RepositoryLanguage> RepositoryLanguages => Set<RepositoryLanguage>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    /// <summary>
    /// Legacy synthetic activity table. No longer written to or read from once
    /// analytics move to ingested data; retained for one phase so the change is
    /// reversible, then dropped.
    /// </summary>
    public DbSet<DevActivity> DevActivities => Set<DevActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrackedRepository>(entity =>
        {
            entity.ToTable("TrackedRepositories");

            // Identity is (provider, external id): the same owner/name can exist
            // on more than one provider, and ids are only unique within one.
            entity.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.Provider, x.FullName }).IsUnique();

            // The sync worker's primary lookup.
            entity.HasIndex(x => new { x.IsActive, x.SyncStatus });

            entity.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Owner).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Language).HasMaxLength(50);
            entity.Property(x => x.HtmlUrl).HasMaxLength(500);
            entity.Property(x => x.DefaultBranch).HasMaxLength(100);
            entity.Property(x => x.SyncStatus).HasMaxLength(20).IsRequired();
            entity.Property(x => x.LastSyncError).HasMaxLength(1000);
            entity.Property(x => x.AddedBy).HasMaxLength(100);
            entity.Property(x => x.CommitsSyncToken).HasMaxLength(200);
            entity.Property(x => x.PullsSyncToken).HasMaxLength(200);
            entity.Property(x => x.Nickname).HasMaxLength(120);
            entity.Property(x => x.Notes).HasMaxLength(1000);

            // The scheduler scans for repositories whose interval has elapsed.
            entity.HasIndex(x => new { x.IsActive, x.LastSyncCompletedAtUtc });
        });

        modelBuilder.Entity<RepositoryLanguage>(entity =>
        {
            // One row per language per repository; a re-sync updates in place.
            entity.HasIndex(x => new { x.RepositoryId, x.Language }).IsUnique();
            entity.Property(x => x.Language).HasMaxLength(80).IsRequired();

            entity.HasOne(x => x.Repository)
                .WithMany(r => r.Languages)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SyncRun>(entity =>
        {
            // History is always read newest-first for one repository, which is
            // also the keyset pagination order.
            entity.HasIndex(x => new { x.RepositoryId, x.StartedAtUtc });

            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Trigger).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Error).HasMaxLength(1000);

            entity.HasOne(x => x.Repository)
                .WithMany(r => r.SyncRuns)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Contributor>(entity =>
        {
            entity.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
            entity.HasIndex(x => x.Login);

            entity.Property(x => x.Provider).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Login).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150);
            entity.Property(x => x.AvatarUrl).HasMaxLength(500);
            entity.Property(x => x.HtmlUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<Commit>(entity =>
        {
            // Idempotent ingestion: re-syncing an overlapping window cannot
            // duplicate a commit. Scoped per repository because a fork shares
            // SHAs with its upstream.
            entity.HasIndex(x => new { x.RepositoryId, x.Sha }).IsUnique();

            // Commit trends: filter by repository, bucket by day.
            entity.HasIndex(x => new { x.RepositoryId, x.CommittedAt });

            // Contributor comparison across a date range.
            entity.HasIndex(x => new { x.ContributorId, x.CommittedAt });

            entity.Property(x => x.Sha).HasMaxLength(64).IsRequired();
            entity.Property(x => x.AuthorName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.AuthorEmail).HasMaxLength(255);
            entity.Property(x => x.Message).HasMaxLength(1000);

            entity.HasOne(x => x.Repository)
                .WithMany(r => r.Commits)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Contributor)
                .WithMany(c => c.Commits)
                .HasForeignKey(x => x.ContributorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PullRequest>(entity =>
        {
            // Re-syncing a PR updates it in place rather than inserting again.
            entity.HasIndex(x => new { x.RepositoryId, x.Number }).IsUnique();

            // PR cycle time is bucketed by merge date.
            entity.HasIndex(x => new { x.RepositoryId, x.MergedAt });
            entity.HasIndex(x => new { x.ContributorId, x.CreatedAt });

            // Incremental PR sync walks this ordering.
            entity.HasIndex(x => new { x.RepositoryId, x.UpdatedAt });

            entity.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(500).IsRequired();
            entity.Property(x => x.State).HasMaxLength(20).IsRequired();
            entity.Property(x => x.AuthorLogin).HasMaxLength(100);

            entity.HasOne(x => x.Repository)
                .WithMany(r => r.PullRequests)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Contributor)
                .WithMany(c => c.PullRequests)
                .HasForeignKey(x => x.ContributorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DevActivity>(entity =>
        {
            entity.HasIndex(x => x.Time);
            entity.HasIndex(x => new { x.Contributor, x.Time });
            entity.HasIndex(x => new { x.Company, x.Time });
        });
    }
}
