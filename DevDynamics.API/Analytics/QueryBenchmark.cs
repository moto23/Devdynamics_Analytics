using System.Diagnostics;
using DevDynamics.API.Data;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Analytics;

public class BenchmarkScenarioResult
{
    public string Scenario { get; set; } = string.Empty;
    public int Iterations { get; set; }

    public double NaiveMedianMs { get; set; }
    public double NaiveMeanMs { get; set; }
    public double OptimizedMedianMs { get; set; }
    public double OptimizedMeanMs { get; set; }

    /// <summary>Reduction in median latency, as a percentage. Can be negative.</summary>
    public double ImprovementPercent { get; set; }

    /// <summary>True when both implementations returned equivalent results.</summary>
    public bool ResultsMatch { get; set; }
}

public class BenchmarkReport
{
    public string Provider { get; set; } = string.Empty;
    public int RowsInTable { get; set; }
    public int Iterations { get; set; }
    public DateTime RunAtUtc { get; set; }
    public List<BenchmarkScenarioResult> Scenarios { get; set; } = [];
    public double AverageImprovementPercent { get; set; }
}

/// <summary>
/// Measures the naive (pre-optimization) implementations against the current
/// ones on the live database.
///
/// Method: each scenario is warmed up first so neither implementation pays for
/// connection setup or query-plan compilation, then run N times alternately.
/// Median is reported alongside mean because a single cloud latency spike
/// skews the mean badly. Results from both implementations are compared so an
/// "optimization" that quietly returns different data cannot look like a win.
/// </summary>
public class QueryBenchmark(AppDbContext context, DatabaseProviderKind provider)
{
    private readonly AppDbContext _context = context;
    private readonly DatabaseProviderKind _provider = provider;

    public async Task<BenchmarkReport> RunAsync(int iterations = 20)
    {
        iterations = Math.Clamp(iterations, 1, 100);

        var queries = new AnalyticsQueries(_context, _provider);

        // The headline claim concerns multi-filter analytics requests, so the
        // filtered scenarios are the ones that matter most.
        var noFilter = new AnalyticsFilter();

        var dateFilter = new AnalyticsFilter
        {
            StartDate = DateTime.UtcNow.AddDays(-180),
            EndDate = DateTime.UtcNow
        };

        var multiFilter = new AnalyticsFilter
        {
            StartDate = DateTime.UtcNow.AddDays(-180),
            EndDate = DateTime.UtcNow,
            Contributor = "Alice",
            Company = "Google"
        };

        var report = new BenchmarkReport
        {
            Provider = _provider.ToString(),
            RowsInTable = await _context.DevActivities.CountAsync(),
            Iterations = iterations,
            RunAtUtc = DateTime.UtcNow
        };

        report.Scenarios.Add(await MeasureAsync(
            "summaryStats - no filter",
            iterations,
            () => queries.NaiveSummaryStatsAsync(noFilter),
            () => queries.OptimizedSummaryStatsAsync(noFilter),
            (a, b) => a.TotalCommits == b.TotalCommits
                      && a.TotalPRs == b.TotalPRs
                      && a.ContributorCount == b.ContributorCount));

        report.Scenarios.Add(await MeasureAsync(
            "summaryStats - date range",
            iterations,
            () => queries.NaiveSummaryStatsAsync(dateFilter),
            () => queries.OptimizedSummaryStatsAsync(dateFilter),
            (a, b) => a.TotalCommits == b.TotalCommits
                      && a.TotalPRs == b.TotalPRs
                      && a.ContributorCount == b.ContributorCount));

        report.Scenarios.Add(await MeasureAsync(
            "summaryStats - multi-filter (date + contributor + company)",
            iterations,
            () => queries.NaiveSummaryStatsAsync(multiFilter),
            () => queries.OptimizedSummaryStatsAsync(multiFilter),
            (a, b) => a.TotalCommits == b.TotalCommits
                      && a.TotalPRs == b.TotalPRs
                      && a.ContributorCount == b.ContributorCount));

        report.Scenarios.Add(await MeasureAsync(
            "prCycleTime - date range",
            iterations,
            () => queries.NaivePRCycleTimeAsync(dateFilter),
            () => queries.OptimizedPRCycleTimeAsync(dateFilter),
            (a, b) => a.Count == b.Count));

        report.Scenarios.Add(await MeasureAsync(
            "prCycleTime - multi-filter (date + company)",
            iterations,
            () => queries.NaivePRCycleTimeAsync(multiFilter),
            () => queries.OptimizedPRCycleTimeAsync(multiFilter),
            (a, b) => a.Count == b.Count));

        report.AverageImprovementPercent = report.Scenarios.Count == 0
            ? 0
            : Math.Round(report.Scenarios.Average(s => s.ImprovementPercent), 1);

        return report;
    }

    private static async Task<BenchmarkScenarioResult> MeasureAsync<T>(
        string scenario,
        int iterations,
        Func<Task<T>> naive,
        Func<Task<T>> optimized,
        Func<T, T, bool> resultsMatch)
    {
        // Warm-up: excluded from the numbers so neither side is charged for
        // connection establishment or first-call plan compilation.
        var naiveWarm = await naive();
        var optimizedWarm = await optimized();

        var naiveTimes = new List<double>(iterations);
        var optimizedTimes = new List<double>(iterations);

        // Alternate the two so any drift in cloud latency hits both equally.
        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await naive();
            sw.Stop();
            naiveTimes.Add(sw.Elapsed.TotalMilliseconds);

            sw = Stopwatch.StartNew();
            await optimized();
            sw.Stop();
            optimizedTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        var naiveMedian = Median(naiveTimes);
        var optimizedMedian = Median(optimizedTimes);

        return new BenchmarkScenarioResult
        {
            Scenario = scenario,
            Iterations = iterations,
            NaiveMedianMs = Math.Round(naiveMedian, 2),
            NaiveMeanMs = Math.Round(naiveTimes.Average(), 2),
            OptimizedMedianMs = Math.Round(optimizedMedian, 2),
            OptimizedMeanMs = Math.Round(optimizedTimes.Average(), 2),
            ImprovementPercent = naiveMedian <= 0
                ? 0
                : Math.Round((naiveMedian - optimizedMedian) / naiveMedian * 100.0, 1),
            ResultsMatch = resultsMatch(naiveWarm, optimizedWarm)
        };
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;

        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
