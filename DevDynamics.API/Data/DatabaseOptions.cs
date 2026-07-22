namespace DevDynamics.API.Data;

/// <summary>
/// Database resilience settings.
///
/// Externalised because the right values depend on the deployment: Azure SQL
/// serverless needs a generous retry budget to cover an auto-pause resume,
/// whereas an always-on instance does not.
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>EF Core transient-failure retries.</summary>
    public int MaxRetryCount { get; set; } = 6;

    public int MaxRetryDelaySeconds { get; set; } = 20;

    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How long startup waits for a paused serverless database to resume before
    /// migrating. Zero disables the wait.
    /// </summary>
    public int StartupWaitAttempts { get; set; } = 12;

    public int StartupWaitIntervalSeconds { get; set; } = 10;
}
