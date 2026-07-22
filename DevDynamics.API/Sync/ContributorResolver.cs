using DevDynamics.API.Data;
using DevDynamics.API.Models;
using DevDynamics.API.SourceControl;
using Microsoft.EntityFrameworkCore;

namespace DevDynamics.API.Sync;

/// <summary>
/// Resolves contributor records to persisted rows in bulk.
///
/// Resolving one contributor at a time cost a query plus a SaveChanges per
/// author — hundreds of round trips per repository, which dominated sync time
/// against a remote database. This resolves a whole batch in one query and one
/// insert, and caches within the sync so repeat authors cost nothing.
/// </summary>
public class ContributorResolver(AppDbContext context, string providerKey)
{
    private readonly Dictionary<string, Contributor> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Ensures every distinct author in the batch exists, returning a lookup
    /// from external id to the persisted contributor.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, Contributor>> ResolveAsync(
        IEnumerable<ContributorRecord?> records,
        CancellationToken cancellationToken)
    {
        var distinct = records
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.ExternalId))
            .Select(r => r!)
            .GroupBy(r => r.ExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var unresolved = distinct.Keys
            .Where(id => !_cache.ContainsKey(id))
            .ToList();

        if (unresolved.Count > 0)
        {
            // One round trip for everything not already cached.
            var existing = await context.Contributors
                .Where(c => c.Provider == providerKey && unresolved.Contains(c.ExternalId))
                .ToListAsync(cancellationToken);

            foreach (var contributor in existing)
            {
                _cache[contributor.ExternalId] = contributor;
            }

            var toCreate = unresolved
                .Where(id => !_cache.ContainsKey(id))
                .Select(id =>
                {
                    var record = distinct[id];

                    return new Contributor
                    {
                        Provider = providerKey,
                        ExternalId = record.ExternalId,
                        Login = record.Login,
                        Name = record.Name,
                        AvatarUrl = record.AvatarUrl,
                        HtmlUrl = record.HtmlUrl,
                        IsBot = record.IsBot
                    };
                })
                .ToList();

            if (toCreate.Count > 0)
            {
                // One insert for every new contributor in the batch.
                context.Contributors.AddRange(toCreate);
                await context.SaveChangesAsync(cancellationToken);

                foreach (var contributor in toCreate)
                {
                    _cache[contributor.ExternalId] = contributor;
                }

                NewContributorCount += toCreate.Count;
            }
        }

        // Identity is the external id, so a changed login is an update, never a
        // second row. Fields absent on an earlier sync are backfilled here:
        // contributors first seen through a pull request carry no display name,
        // and pick one up the first time they appear as a commit author.
        foreach (var (externalId, record) in distinct)
        {
            if (!_cache.TryGetValue(externalId, out var contributor))
            {
                continue;
            }

            if (!string.Equals(contributor.Login, record.Login, StringComparison.Ordinal))
            {
                contributor.Login = record.Login;
            }

            contributor.Name ??= record.Name;
            contributor.AvatarUrl ??= record.AvatarUrl;
            contributor.HtmlUrl ??= record.HtmlUrl;
        }

        return _cache;
    }

    public int NewContributorCount { get; private set; }

    public int? IdFor(ContributorRecord? record) =>
        record is not null && _cache.TryGetValue(record.ExternalId, out var contributor)
            ? contributor.Id
            : null;
}
