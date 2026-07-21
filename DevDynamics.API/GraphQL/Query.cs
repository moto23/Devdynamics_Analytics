using DevDynamics.API.Analytics;
using DevDynamics.API.Data;
using DevDynamics.API.Models;

namespace DevDynamics.API.GraphQL;

/// <summary>
/// GraphQL entry points. The schema is unchanged from Phase 1 — only the
/// execution strategy underneath changed, so existing clients are unaffected.
/// </summary>
public class Query
{
    public Task<List<DevActivity>> GetDevActivities(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? company = null)
    {
        return new AnalyticsQueries(context, provider.Kind)
            .OptimizedDevActivitiesAsync(new AnalyticsFilter
            {
                StartDate = startDate,
                EndDate = endDate,
                Contributor = contributor,
                Company = company
            });
    }

    public Task<List<PRCycleTimeResult>> GetPRCycleTime(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? company = null)
    {
        return new AnalyticsQueries(context, provider.Kind)
            .OptimizedPRCycleTimeAsync(new AnalyticsFilter
            {
                StartDate = startDate,
                EndDate = endDate,
                Company = company
            });
    }

    public Task<SummaryResult> GetSummaryStats(
        [Service] AppDbContext context,
        [Service] DatabaseProviderInfo provider,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? contributor = null,
        string? company = null)
    {
        return new AnalyticsQueries(context, provider.Kind)
            .OptimizedSummaryStatsAsync(new AnalyticsFilter
            {
                StartDate = startDate,
                EndDate = endDate,
                Contributor = contributor,
                Company = company
            });
    }
}
