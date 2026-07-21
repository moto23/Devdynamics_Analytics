using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DevDynamics.API.Data;

/// <summary>
/// Used only by "dotnet ef" at design time.
///
/// Migrations are provider-specific, and SQL Server is the production database,
/// so this factory always targets SQL Server. That guarantees "dotnet ef
/// migrations add" emits T-SQL rather than SQLite DDL, regardless of which
/// connection string happens to be configured on the developer's machine.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default");

        // Migrations are generated from the model, not from a live database, so a
        // placeholder is fine when no real SQL Server connection string is present.
        if (DatabaseProvider.Detect(connectionString) != DatabaseProviderKind.SqlServer)
        {
            connectionString = "Server=(localdb)\\design-time;Database=DevDynamicsDesignTime;Trusted_Connection=True;";
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
