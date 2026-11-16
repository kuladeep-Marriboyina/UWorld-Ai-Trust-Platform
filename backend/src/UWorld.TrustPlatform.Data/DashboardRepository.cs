using Microsoft.Data.SqlClient;

namespace UWorld.TrustPlatform.Data;

public sealed record ActiveSubscription(
    int SubscriptionId, int UserId, int AccessCount, int DistinctCountries, string? LastCountry, DateTime LastAccessUtc);

public interface IDashboardRepository
{
    Task<IReadOnlyList<ActiveSubscription>> GetActiveSubscriptionsAsync(int top, int minutes, CancellationToken ct = default);
}

public sealed class DashboardRepository : IDashboardRepository
{
    private readonly ISqlConnectionFactory _factory;
    public DashboardRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<ActiveSubscription>> GetActiveSubscriptionsAsync(int top, int minutes, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@top)
                   l.SubscriptionId,
                   MAX(l.UserId) AS UserId,
                   COUNT(*) AS AccessCount,
                   COUNT(DISTINCT l.CountryCode) AS DistinctCountries,
                   MAX(l.DateCreated) AS LastAccess,
                   (SELECT TOP 1 l2.CountryCode FROM dbo.USER_LOGS l2
                     WHERE l2.SubscriptionId = l.SubscriptionId ORDER BY l2.DateCreated DESC) AS LastCountry
            FROM dbo.USER_LOGS l
            WHERE l.SubscriptionId IS NOT NULL
              AND l.DateCreated >= DATEADD(MINUTE, -@minutes, SYSUTCDATETIME())
            GROUP BY l.SubscriptionId
            ORDER BY COUNT(*) DESC";

        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@top", top);
        cmd.Parameters.AddWithValue("@minutes", minutes);

        var list = new List<ActiveSubscription>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ActiveSubscription(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetDateTime(4)));
        return list;
    }
}
