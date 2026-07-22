using DevDynamics.API.Analytics;
using DevDynamics.API.Data;
using DevDynamics.API.Models;
using DevDynamics.API.Sync;

namespace DevDynamics.API.GraphQL;

/// <summary>
/// Public analytics and registry queries.
///
/// Every analytics field is scoped to whichever repositories are currently
/// active in the registry, so the dashboard follows the tracked set with no
/// code change as repositories are added or removed.
/// </summary>
public class Query
{
    private static RepositoryFilter BuildFilter(
        DateTime? startDate,
        DateTime? endDate,
        string? contributor,
        string? repository,
        bool excludeBots) => new()
        {
            StartDate = startDate,
            EndDate = endDate,
            Contributor = contributor,
            Repository = repository,
            ExcludeBots = excludeBots
        };

    /// <summary>Repositories in the registry, with their sync state.</summary>
    public Task<List<TrackedRepository>> GetTrackedRepositories(
        [Service] RepositoryRegistryService registry,
        bool includeInactive = false)
        => registry.GetAllAsync(includeInactive);

    public Task<GitHubSummary> GetSummaryStats(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => new GitHubAnalyticsQueries(context, provider.Kind)
            .GetSummaryAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    /// <summary>Commits per day — the commit trends chart.</summary>
    public Task<List<CommitTrendPoint>> GetCommitTrends(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => new GitHubAnalyticsQueries(context, provider.Kind)
            .GetCommitTrendsAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    /// <summary>
    /// Average hours from opening to merge, bucketed by merge date and counting
    /// merged pull requests only.
    /// </summary>
    public Task<List<PRCyclePoint>> GetPrCycleTime(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => new GitHubAnalyticsQueries(context, provider.Kind)
            .GetPRCycleTimeAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    /// <summary>Contributors in the filtered data, ranked by commits.</summary>
    public Task<List<ContributorSummary>> GetContributors(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? repository = null,
        bool excludeBots = false)
        => new GitHubAnalyticsQueries(context, provider.Kind)
            .GetContributorsAsync(BuildFilter(startDate, endDate, null, repository, excludeBots));
}
