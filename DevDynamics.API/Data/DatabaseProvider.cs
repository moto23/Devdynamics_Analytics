namespace DevDynamics.API.Data;

public enum DatabaseProviderKind
{
    Sqlite,
    SqlServer
}

/// <summary>
/// Reference-type wrapper so the provider can be resolved from DI
/// (an enum cannot be registered as a service on its own).
/// </summary>
public sealed class DatabaseProviderInfo(DatabaseProviderKind kind)
{
    public DatabaseProviderKind Kind { get; } = kind;
}

/// <summary>
/// Single source of truth for which EF Core provider a connection string implies.
/// Program.cs and the design-time migration factory both use this so the running
/// app and the generated migrations can never disagree.
/// </summary>
public static class DatabaseProvider
{
    /// <summary>
    /// SQL Server connection strings carry "Server=" and/or "Initial Catalog=".
    /// A SQLite one is just "Data Source=file.db", so the two are unambiguous.
    /// </summary>
    public static DatabaseProviderKind Detect(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return DatabaseProviderKind.Sqlite;
        }

        var isSqlServer =
            connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase);

        return isSqlServer
            ? DatabaseProviderKind.SqlServer
            : DatabaseProviderKind.Sqlite;
    }
}
