using DevDynamics.API.Models;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DevActivity> DevActivities => Set<DevActivity>();

    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<Contributor> Contributors => Set<Contributor>();
    public DbSet<Commit> Commits => Set<Commit>();
    public DbSet<PullRequest> PullRequests => Set<PullRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Indexes match the analytics access patterns: every dashboard query
        // filters by a date range and optionally by repository and/or contributor.

        modelBuilder.Entity<Repository>(entity =>
        {
            entity.HasIndex(x => x.FullName).IsUnique();
            entity.HasIndex(x => x.GitHubId).IsUnique();
            entity.Property(x => x.FullName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.Owner).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Language).HasMaxLength(50);
        });

        modelBuilder.Entity<Contributor>(entity =>
        {
            entity.HasIndex(x => x.Login).IsUnique();
            entity.HasIndex(x => x.GitHubId).IsUnique();
            entity.Property(x => x.Login).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150);
        });

        modelBuilder.Entity<Commit>(entity =>
        {
            entity.HasIndex(x => x.Sha).IsUnique();

            // Commit trends: filter by repo, bucket by date.
            entity.HasIndex(x => new { x.RepositoryId, x.CommittedAt });

            // Contributor comparison over a date range.
            entity.HasIndex(x => new { x.ContributorId, x.CommittedAt });

            entity.Property(x => x.Sha).HasMaxLength(40).IsRequired();
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
            entity.HasIndex(x => x.GitHubId).IsUnique();
            entity.HasIndex(x => new { x.RepositoryId, x.Number }).IsUnique();

            // PR cycle time is bucketed by merge date.
            entity.HasIndex(x => new { x.RepositoryId, x.MergedAt });
            entity.HasIndex(x => new { x.ContributorId, x.CreatedAt });

            entity.Property(x => x.Title).HasMaxLength(500).IsRequired();
            entity.Property(x => x.State).HasMaxLength(20).IsRequired();

            entity.HasOne(x => x.Repository)
                .WithMany(r => r.PullRequests)
                .HasForeignKey(x => x.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Contributor)
                .WithMany(c => c.PullRequests)
                .HasForeignKey(x => x.ContributorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Legacy synthetic table. Indexed so the existing resolvers stay usable
        // until GitHub-backed analytics replace them in a later phase.
        modelBuilder.Entity<DevActivity>(entity =>
        {
            entity.HasIndex(x => x.Time);
            entity.HasIndex(x => new { x.Contributor, x.Time });
            entity.HasIndex(x => new { x.Company, x.Time });
        });
    }
}
