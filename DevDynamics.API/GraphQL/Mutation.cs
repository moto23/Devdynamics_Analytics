using DevDynamics.API.Sync;
using HotChocolate;

namespace DevDynamics.API.GraphQL;

public class RepositoryMutationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? RepositoryId { get; set; }
    public string? FullName { get; set; }
    public string? SyncStatus { get; set; }
}

/// <summary>
/// Repository management.
///
/// These mutations spend GitHub rate limit and database compute, and the API is
/// public, so every one of them requires the admin key. Analytics queries stay
/// unauthenticated so the dashboard needs no credentials.
/// </summary>
public class Mutation
{
    /// <summary>Tracks any repository the configured provider can see.</summary>
    public async Task<RepositoryMutationResult> AddRepository(
        [Service] RepositoryRegistryService registry,
        [Service] IAdminAuthorizer admin,
        string fullName,
        string provider = "GitHub")
    {
        admin.EnsureAuthorized();

        var result = await registry.AddAsync(fullName, provider, addedBy: "admin");
        return Map(result);
    }

    /// <summary>Removes a repository and its ingested data.</summary>
    public async Task<RepositoryMutationResult> RemoveRepository(
        [Service] RepositoryRegistryService registry,
        [Service] IAdminAuthorizer admin,
        int id)
    {
        admin.EnsureAuthorized();

        var result = await registry.RemoveAsync(id);
        return Map(result);
    }

    /// <summary>
    /// Updates user-managed metadata. Ingested fields are never editable here:
    /// they are overwritten by the next sync, so allowing edits would be a lie.
    /// </summary>
    public async Task<RepositoryMutationResult> UpdateRepository(
        [Service] RepositoryRegistryService registry,
        [Service] IAdminAuthorizer admin,
        int id,
        string? nickname = null,
        string? notes = null,
        bool? isPinned = null,
        int? syncIntervalMinutes = null)
    {
        admin.EnsureAuthorized();

        var result = await registry.UpdateAsync(id, nickname, notes, isPinned, syncIntervalMinutes);
        return Map(result);
    }

    /// <summary>Enables or disables without discarding ingested history.</summary>
    public async Task<RepositoryMutationResult> SetRepositoryActive(
        [Service] RepositoryRegistryService registry,
        [Service] IAdminAuthorizer admin,
        int id,
        bool isActive)
    {
        admin.EnsureAuthorized();

        var result = await registry.SetActiveAsync(id, isActive);
        return Map(result);
    }

    /// <summary>Queues one repository for incremental re-sync.</summary>
    public async Task<RepositoryMutationResult> SyncRepository(
        [Service] RepositoryRegistryService registry,
        [Service] IAdminAuthorizer admin,
        int id)
    {
        admin.EnsureAuthorized();

        var result = await registry.QueueSyncAsync(id);
        return Map(result);
    }

    /// <summary>Queues every active repository, however many there are.</summary>
    public async Task<RepositoryMutationResult> SyncAllRepositories(
        [Service] RepositoryRegistryService registry,
        [Service] IAdminAuthorizer admin)
    {
        admin.EnsureAuthorized();

        var result = await registry.QueueSyncAllAsync();
        return Map(result);
    }

    private static RepositoryMutationResult Map(RegistryActionResult result) => new()
    {
        Success = result.Success,
        Message = result.Message ?? string.Empty,
        RepositoryId = result.Repository?.Id,
        FullName = result.Repository?.FullName,
        SyncStatus = result.Repository?.SyncStatus
    };
}

/// <summary>
/// Guards management mutations with a shared admin key supplied as the
/// X-Admin-Key header and configured via Admin__ApiKey.
/// </summary>
public interface IAdminAuthorizer
{
    void EnsureAuthorized();
}

public class AdminAuthorizer(
    IHttpContextAccessor accessor,
    IConfiguration configuration) : IAdminAuthorizer
{
    public const string HeaderName = "X-Admin-Key";

    public void EnsureAuthorized()
    {
        var configuredKey = configuration["Admin:ApiKey"];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            // Fail closed. An unset key must disable management entirely rather
            // than leave the mutations open to anyone.
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Repository management is disabled: no admin key is configured.")
                    .SetCode("ADMIN_KEY_NOT_CONFIGURED")
                    .Build());
        }

        var supplied = accessor.HttpContext?.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(supplied) || !FixedTimeEquals(supplied, configuredKey))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Unauthorized. Supply a valid {HeaderName} header.")
                    .SetCode("UNAUTHORIZED")
                    .Build());
        }
    }

    /// <summary>Length-constant comparison so the key cannot be probed by timing.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(a);
        var right = System.Text.Encoding.UTF8.GetBytes(b);

        return left.Length == right.Length
               && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }
}
