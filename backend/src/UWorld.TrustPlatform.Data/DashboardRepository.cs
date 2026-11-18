using Microsoft.Data.SqlClient;

namespace UWorld.TrustPlatform.Data;

public sealed record ActiveSubscription(
    int SubscriptionId, int UserId, int AccessCount, int DistinctCountries, string? LastCountry, DateTime LastAccessUtc);

/// <summary>One hour of customer traffic: what normal looks like next to what got flagged.</summary>
public sealed record TrafficBucket(DateTime HourUtc, int Signups, int Logins, int FailedLogins, int Subscriptions, int CasesRaised);

public sealed record CountryTraffic(string Country, int Requests, int Failed, int Signups);

public sealed record TrafficSummary(
    IReadOnlyList<TrafficBucket> Hourly, IReadOnlyList<CountryTraffic> Countries,
    int Signups, int ApiCalls, int FailedLogins, int Payments, int AccessEvents);

public interface IDashboardRepository
{
    Task<IReadOnlyList<ActiveSubscription>> GetActiveSubscriptionsAsync(int top, int minutes, CancellationToken ct = default);
    Task<TrafficSummary> GetTrafficAsync(int hours, CancellationToken ct = default);
    Task<IReadOnlyList<Hotspot>> GetHotspotsAsync(int take, CancellationToken ct = default);
}

/// <summary>An identifier (IP, device, card, domain, code) sitting behind open risk.</summary>
public sealed record Hotspot(string Type, string Value, int Cases, decimal AtRiskUsd, DateTime LastSeenUtc, string TopSeverity, int Accounts);

public sealed class DashboardRepository : IDashboardRepository
{
    private readonly ISqlConnectionFactory _factory;
    public DashboardRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<ActiveSubscription>> GetActiveSubscriptionsAsync(int top, int minutes, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@top)
                   l.SubscriptionId, MAX(l.UserId) AS UserId, COUNT(*) AS AccessCount, COUNT(DISTINCT l.CountryCode) AS DistinctCountries,
                   MAX(l.DateCreated) AS LastAccess,
                   (SELECT TOP 1 l2.CountryCode FROM dbo.USER_LOGS l2 WHERE l2.SubscriptionId = l.SubscriptionId ORDER BY l2.DateCreated DESC) AS LastCountry
            FROM dbo.USER_LOGS l
            WHERE l.SubscriptionId IS NOT NULL AND l.DateCreated >= DATEADD(MINUTE, -@minutes, SYSUTCDATETIME())
            GROUP BY l.SubscriptionId
            ORDER BY COUNT(DISTINCT l.CountryCode) DESC, COUNT(*) DESC";

        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@top", top);
        cmd.Parameters.AddWithValue("@minutes", minutes);
        var list = new List<ActiveSubscription>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ActiveSubscription(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.IsDBNull(5) ? null : r.GetString(5), r.GetDateTime(4)));
        return list;
    }

    public async Task<IReadOnlyList<Hotspot>> GetHotspotsAsync(int take, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@take) e.EntityType, e.EntityValue, COUNT(DISTINCT c.CaseId), SUM(c.BusinessImpactUsd), MAX(COALESCE(c.UpdatedUtc, c.CreatedUtc)),
                   MIN(CASE c.Severity WHEN 'Critical' THEN 0 WHEN 'High' THEN 1 WHEN 'Medium' THEN 2 ELSE 3 END),
                   (SELECT COUNT(DISTINCT x.EntityValue) FROM dbo.CaseEntities x WHERE x.EntityType = 'user'
                       AND x.CaseId IN (SELECT y.CaseId FROM dbo.CaseEntities y WHERE y.EntityType = e.EntityType AND y.EntityValue = e.EntityValue))
            FROM dbo.CaseEntities e JOIN dbo.RiskCases c ON c.CaseId = e.CaseId
            WHERE c.Status <> 'Resolved' AND e.EntityType IN ('ip','device','card','domain','code')
            GROUP BY e.EntityType, e.EntityValue
            ORDER BY MIN(CASE c.Severity WHEN 'Critical' THEN 0 WHEN 'High' THEN 1 WHEN 'Medium' THEN 2 ELSE 3 END), COUNT(DISTINCT c.CaseId) DESC, SUM(c.BusinessImpactUsd) DESC";
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<Hotspot>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        string[] sev = { "Critical", "High", "Medium", "Low" };
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new Hotspot(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetDecimal(3), r.GetDateTime(4), sev[r.GetInt32(5)], r.GetInt32(6)));
        return list;
    }

    public async Task<TrafficSummary> GetTrafficAsync(int hours, CancellationToken ct = default)
    {
        const string sql = @"
DECLARE @since DATETIME2 = DATEADD(HOUR, -@hours, SYSUTCDATETIME());
DECLARE @start DATETIME2 = DATEADD(HOUR, DATEDIFF(HOUR, 0, @since), 0);

;WITH h AS (SELECT @start AS HourUtc UNION ALL SELECT DATEADD(HOUR, 1, HourUtc) FROM h WHERE HourUtc < DATEADD(HOUR, -1, SYSUTCDATETIME()))
SELECT h.HourUtc,
  (SELECT COUNT(*) FROM dbo.USERS u WHERE u.DateCreated >= h.HourUtc AND u.DateCreated < DATEADD(HOUR,1,h.HourUtc)),
  (SELECT COUNT(*) FROM dbo.API_LOGS a WHERE a.Endpoint LIKE '%/auth/login%' AND a.Outcome='Success' AND a.DateCreated >= h.HourUtc AND a.DateCreated < DATEADD(HOUR,1,h.HourUtc)),
  (SELECT COUNT(*) FROM dbo.API_LOGS a WHERE a.Endpoint LIKE '%/auth/login%' AND a.Outcome='Failed' AND a.DateCreated >= h.HourUtc AND a.DateCreated < DATEADD(HOUR,1,h.HourUtc)),
  (SELECT COUNT(*) FROM dbo.USER_SUBSCRIPTIONS s WHERE s.DateActivated >= h.HourUtc AND s.DateActivated < DATEADD(HOUR,1,h.HourUtc)),
  (SELECT COUNT(*) FROM dbo.RiskCases c WHERE c.CreatedUtc >= h.HourUtc AND c.CreatedUtc < DATEADD(HOUR,1,h.HourUtc))
FROM h OPTION (MAXRECURSION 1000);

SELECT TOP 12 CountryCode, COUNT(*), SUM(CASE WHEN Outcome='Failed' THEN 1 ELSE 0 END), SUM(CASE WHEN Endpoint LIKE '%/auth/register%' THEN 1 ELSE 0 END)
FROM dbo.API_LOGS WHERE DateCreated >= @since AND CountryCode IS NOT NULL GROUP BY CountryCode ORDER BY COUNT(*) DESC;

SELECT (SELECT COUNT(*) FROM dbo.USERS WHERE DateCreated >= @since),
       (SELECT COUNT(*) FROM dbo.API_LOGS WHERE DateCreated >= @since),
       (SELECT COUNT(*) FROM dbo.API_LOGS WHERE DateCreated >= @since AND Outcome='Failed' AND Endpoint LIKE '%/auth/login%'),
       (SELECT COUNT(*) FROM dbo.USER_PAYMENTS WHERE DateCreated >= @since),
       (SELECT COUNT(*) FROM dbo.USER_LOGS WHERE DateCreated >= @since);";

        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@hours", hours);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var hourly = new List<TrafficBucket>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            hourly.Add(new TrafficBucket(DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5)));
        await r.NextResultAsync(ct).ConfigureAwait(false);
        var countries = new List<CountryTraffic>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            countries.Add(new CountryTraffic(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
        await r.NextResultAsync(ct).ConfigureAwait(false);
        await r.ReadAsync(ct).ConfigureAwait(false);
        return new TrafficSummary(hourly, countries, r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4));
    }
}
