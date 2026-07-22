using System.Text;
using System.Text.Json;

namespace DevDynamics.API.Analytics;

public class PageInfo
{
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
    public string? EndCursor { get; set; }

    /// <summary>
    /// Total matching rows. Costs an extra COUNT, so resolvers that do not need
    /// it leave it null rather than paying for it on every page.
    /// </summary>
    public int? TotalCount { get; set; }
}

/// <summary>
/// A page of results plus its metadata. Every list resolver returns this rather
/// than a bare array, so a growing dataset never turns into an unbounded
/// response.
/// </summary>
public class Connection<T>
{
    public List<T> Items { get; set; } = [];
    public PageInfo PageInfo { get; set; } = new();
}

/// <summary>
/// Opaque pagination cursors.
///
/// Two strategies, chosen per resolver by what the data allows:
///
///   Keyset — for entity lists ordered by a stored, indexed column (commits by
///   date, pull requests by update time). The cursor carries the last row's
///   sort key and id, so the next page is an index seek regardless of depth.
///   This is what keeps hundreds of thousands of rows queryable.
///
///   Offset — for rankings whose sort key is a computed aggregate (commits per
///   contributor). Keyset would require the aggregate itself to be indexed,
///   which it cannot be. These sets are bounded by the number of contributors
///   or repositories rather than events, so the depth stays small.
///
/// Both are base64 and opaque to clients, so a resolver can move from offset to
/// keyset later without a breaking change.
/// </summary>
public static class Cursor
{
    public static string EncodeOffset(int offset) =>
        Encode(new CursorPayload { Offset = offset });

    public static int DecodeOffset(string? cursor)
    {
        var payload = Decode(cursor);
        return payload?.Offset ?? 0;
    }

    public static string EncodeKeyset(DateTime sortKey, int id) =>
        Encode(new CursorPayload { SortDate = sortKey, Id = id });

    public static (DateTime SortKey, int Id)? DecodeKeyset(string? cursor)
    {
        var payload = Decode(cursor);

        return payload?.SortDate is null || payload.Id is null
            ? null
            : (payload.SortDate.Value, payload.Id.Value);
    }

    /// <summary>Clamps a requested page size into a sane, server-controlled range.</summary>
    public static int PageSize(int? requested, int fallback = 25, int max = 100) =>
        Math.Clamp(requested ?? fallback, 1, max);

    private static string Encode(CursorPayload payload) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private static CursorPayload? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return JsonSerializer.Deserialize<CursorPayload>(json);
        }
        catch
        {
            // A malformed cursor restarts from the beginning rather than erroring:
            // a stale bookmark should not break the page.
            return null;
        }
    }

    private class CursorPayload
    {
        public int Offset { get; set; }
        public DateTime? SortDate { get; set; }
        public int? Id { get; set; }
    }
}
