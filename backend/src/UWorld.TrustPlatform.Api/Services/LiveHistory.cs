using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Data;

namespace UWorld.TrustPlatform.Api.Services;

/// <summary>Seeds the live feed with the last day's customer API calls and raised cases at startup.</summary>
public static class LiveHistory
{
    public static async Task HydrateAsync(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<ISqlConnectionFactory>();
        var hub = sp.GetRequiredService<LiveEventHub>();
        var items = new List<(string, string, string, string?, DateTime, int?, string?)>();
        await using var cn = await factory.OpenAsync();
        await using (var cmd = new SqlCommand(@"SELECT TOP 40 Endpoint, Outcome, Email, UserId, IPAddress, DateCreated FROM dbo.API_LOGS
                                                WHERE DateCreated >= DATEADD(DAY, -1, SYSUTCDATETIME()) ORDER BY DateCreated DESC", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var ep = r.GetString(0); var ok = r.GetString(1) == "Success"; var email = r.IsDBNull(2) ? "?" : r.GetString(2);
                var ip = r.GetString(4);
                var (type, title) = ep.Contains("register") ? ("signup", $"New account{(r.IsDBNull(3) ? "" : " #" + r.GetInt32(3))}: {email}")
                    : ep.Contains("login") ? (ok ? "login" : "login-failed", ok ? $"Login: {email}" : $"Failed login for {email}")
                    : ("subscribe", $"Account #{(r.IsDBNull(3) ? 0 : r.GetInt32(3))} subscribed");
                items.Add((type, ok ? "info" : "low", title, $"from {ip}", r.GetDateTime(5), null, ip));
            }
        await using (var cmd = new SqlCommand(@"SELECT TOP 15 CaseId, Title, Severity, RuleId, CreatedUtc FROM dbo.RiskCases
                                                WHERE CreatedUtc >= DATEADD(DAY, -1, SYSUTCDATETIME()) ORDER BY CreatedUtc DESC", cn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
                items.Add(("case-created", r.GetString(2).ToLowerInvariant(), r.GetString(1), $"raised by {r.GetString(3)}", r.GetDateTime(4), r.GetInt32(0), null));
        hub.Hydrate(items);
    }
}
