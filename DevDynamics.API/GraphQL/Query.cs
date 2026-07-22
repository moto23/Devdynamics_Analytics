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
///
/// List fields return a paginated connection rather than a whole collection:
/// contributor and commit counts grow without bound, and an unbounded response
/// would eventually be the thing that breaks.
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

    private static GitHubAnalyticsQueries Analytics(AppDbContext context, DatabaseProviderInfo provider) =>
        new(context, provider.Kind);

    // =====================================================================
    // Registry
    // =====================================================================

    /// <summary>Full registry listing. Bounded by the number of tracked repositories.</summary>
    public Task<List<TrackedRepository>> GetTrackedRepositories(
        [Service] RepositoryRegistryService registry,
        bool includeInactive = false)
        => registry.GetAllAsync(includeInactive);

    /// <summary>Paginated, searchable, sortable registry listing.</summary>
    public Task<Connection<TrackedRepository>> GetRepositories(
        [Service] RepositoryRegistryService registry,
        bool includeInactive = false,
        string? search = null,
        string? sortBy = null,
        bool descending = true,
        string? after = null,
        int? first = null)
        => registry.GetPageAsync(includeInactive, search, sortBy, descending, after, first);

    /// <summary>Synchronisation history for one repository, newest first.</summary>
    public Task<Connection<SyncRun>> GetSyncRuns(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        int repositoryId,
        string? after = null,
        int? first = null)
        => Analytics(context, provider).GetSyncRunsAsync(repositoryId, after, first);

    // =====================================================================
    // Summary and trends
    // =====================================================================

    public Task<GitHubSummary> GetSummaryStats(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => Analytics(context, provider)
            .GetSummaryAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    /// <summary>Commits per day.</summary>
    public Task<List<CommitTrendPoint>> GetCommitTrends(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => Analytics(context, provider)
            .GetCommitTrendsAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    /// <summary>
    /// Average hours from opening to merge, bucketed by merge date over merged
    /// pull requests only.
    /// </summary>
    public Task<List<PRCyclePoint>> GetPrCycleTime(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => Analytics(context, provider)
            .GetPRCycleTimeAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    /// <summary>Daily commit counts for a calendar heatmap.</summary>
    public Task<List<HeatmapDay>> GetContributionHeatmap(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => Analytics(context, provider)
            .GetHeatmapAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    /// <summary>Commits bucketed by weekday and hour.</summary>
    public Task<List<ActivityCell>> GetActivityDistribution(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false)
        => Analytics(context, provider)
            .GetActivityDistributionAsync(BuildFilter(startDate, endDate, contributor, repository, excludeBots));

    // =====================================================================
    // Contributors
    // =====================================================================

    /// <summary>Ranked contributor statistics, paginated and searchable.</summary>
    public Task<Connection<ContributorStats>> GetContributors(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? repository = null,
        bool excludeBots = false,
        string? search = null,
        string? sortBy = null,
        bool descending = true,
        string? after = null,
        int? first = null)
        => Analytics(context, provider).GetContributorsAsync(
            BuildFilter(startDate, endDate, null, repository, excludeBots),
            search, sortBy ?? "commits", descending, after, first);

    /// <summary>One contributor, with their repository split and heatmap.</summary>
    public Task<ContributorDetail?> GetContributorDetail(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        string login,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? repository = null)
        => Analytics(context, provider)
            .GetContributorDetailAsync(login, BuildFilter(startDate, endDate, null, repository, false));

    // =====================================================================
    // Repository insights
    // =====================================================================

    /// <summary>
    /// Health metrics for one repository. Every value is derived from ingested
    /// data; nothing here is estimated.
    /// </summary>
    public Task<RepositoryHealth?> GetRepositoryHealth(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        string fullName,
        DateTime? startDate = null,
        DateTime? endDate = null,
        bool excludeBots = false)
        => Analytics(context, provider)
            .GetRepositoryHealthAsync(fullName, BuildFilter(startDate, endDate, null, null, excludeBots));

    /// <summary>Language breakdown by bytes, across all repositories or one.</summary>
    public Task<List<LanguageSlice>> GetLanguageDistribution(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        string? repository = null)
        => Analytics(context, provider).GetLanguageDistributionAsync(repository);

    // =====================================================================
    // Entity lists (keyset paginated)
    // =====================================================================

    public Task<Connection<Commit>> GetCommits(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false,
        string? after = null,
        int? first = null)
        => Analytics(context, provider).GetCommitsAsync(
            BuildFilter(startDate, endDate, contributor, repository, excludeBots), after, first);

    public Task<Connection<PullRequest>> GetPullRequests(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? repository = null,
        bool excludeBots = false,
        string? state = null,
        string? after = null,
        int? first = null)
        => Analytics(context, provider).GetPullRequestsAsync(
            BuildFilter(startDate, endDate, contributor, repository, excludeBots), state, after, first);
}
