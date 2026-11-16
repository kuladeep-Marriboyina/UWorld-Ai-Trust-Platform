using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Ingestion;

namespace UWorld.TrustPlatform.Api.Services;

public sealed record SimulationResult(string Scenario, string CorrelationId, int CasesCreated);

/// <summary>
/// Injects a fresh anomaly into the source data and runs one ingestion pass, so a real
/// RiskCase appears live on stage during the demo. Each call uses a unique correlation id.
/// </summary>
public sealed class DemoSimulator
{
    private readonly ISqlConnectionFactory _factory;
    private readonly IngestionWorker _worker;

    public DemoSimulator(ISqlConnectionFactory factory, IngestionWorker worker)
    {
        _factory = factory;
        _worker = worker;
    }

    public async Task<SimulationResult> SimulateAuthorizationGapAsync(string requestId, CancellationToken ct)
    {
        var correlation = "C-SIM-" + Guid.NewGuid().ToString("N")[..8];
        await using var cn = await _factory.OpenAsync(ct);

        var accessId = await NextIdAsync(cn, "AccessEvents", "AccessId", ct);
        var authId = await NextIdAsync(cn, "AuthOutcomes", "AuthId", ct);
        var resource = $"/qbank/package/40/q/{Random.Shared.Next(100, 999)}";

        await using (var cmd = new SqlCommand(
            @"INSERT dbo.AuthOutcomes (AuthId, AccountId, Resource, AuthResult, CorrelationId, OccurredUtc)
              VALUES (@id, 4, @res, 'Denied', @corr, SYSUTCDATETIME())", cn))
        {
            cmd.Parameters.AddWithValue("@id", authId);
            cmd.Parameters.AddWithValue("@res", resource);
            cmd.Parameters.AddWithValue("@corr", correlation);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new SqlCommand(
            @"INSERT dbo.AccessEvents (AccessId, AccountId, Resource, PackageId, QuestionId, SessionId, ContentReturnedBytes, IpAddress, DeviceId, CorrelationId, OccurredUtc)
              VALUES (@id, 4, @res, 40, @q, @sess, 61000, '198.51.100.200', 'DEV-SIM', @corr, SYSUTCDATETIME())", cn))
        {
            cmd.Parameters.AddWithValue("@id", accessId);
            cmd.Parameters.AddWithValue("@res", resource);
            cmd.Parameters.AddWithValue("@q", Random.Shared.Next(100, 999));
            cmd.Parameters.AddWithValue("@sess", "SIM-" + correlation);
            cmd.Parameters.AddWithValue("@corr", correlation);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var created = await _worker.RunOnceAsync(ct, requestId);
        return new SimulationResult("authorization-gap", correlation, created);
    }

    /// <summary>Phase-2 scenarios: inject a fresh fraud row of the chosen type, then run one pass.</summary>
    public async Task<SimulationResult> SimulateScenarioAsync(string scenario, string requestId, CancellationToken ct)
    {
        if (scenario is "authorization-gap") return await SimulateAuthorizationGapAsync(requestId, ct);
        var tag = Guid.NewGuid().ToString("N")[..6];
        await using var cn = await _factory.OpenAsync(ct);

        switch (scenario)
        {
            case "paid-without-braintree":
            {
                var id = await NextIdAsync(cn, "USER_PAYMENTS", "PaymentId", ct);
                await Exec(cn, ct,
                    @"INSERT dbo.USER_PAYMENTS (PaymentId,UserId,FinalAmount,TransactionAmount,DiscountAmount,TransactionId,PaymentStatus,GatewayId,PaymentModeId,ResponseCode,SourceId,PaymentTypeId,IPAddress,CountryCode,DateCreated,DatePaymentReceived)
                      VALUES (@id, 120, 250.00, 250.00, 0, '0', 1, 4, 1, 1, 2, 1, '203.0.113.200', 'US', SYSUTCDATETIME(), SYSUTCDATETIME())",
                    ("@id", id));
                break;   // no BRAINTREE_TRANSACTIONS row → flagged
            }
            case "region-hop":
            {
                var sub = 5900 + Random.Shared.Next(1, 90);
                await Exec(cn, ct,
                    @"INSERT dbo.USER_SUBSCRIPTIONS (SubscriptionId,UserId,CourseId,PackageId,StatusId,Active,DateActivated,DateExpiration,DateFirstAccessed)
                      VALUES (@sub, 120, 1, 10, 1, 1, DATEADD(DAY,-10,SYSUTCDATETIME()), DATEADD(DAY,+50,SYSUTCDATETIME()), DATEADD(DAY,-9,SYSUTCDATETIME()))",
                    ("@sub", sub));
                await Exec(cn, ct,
                    @"INSERT dbo.USER_LOGS (UserId,SubscriptionId,IPAddress,CountryCode,DeviceTypeId,LogType,DateCreated) VALUES
                      (120, @sub, '72.14.9.9', 'US', 1, 'Access', DATEADD(MINUTE,-40,SYSUTCDATETIME())),
                      (120, @sub, '49.36.9.9', 'IN', 2, 'Access', SYSUTCDATETIME())",
                    ("@sub", sub));
                break;
            }
            case "code-over-limit":
            {
                var id = await NextIdAsync(cn, "NCLEX_REFERRAL_CODES", "ReferralId", ct);
                await Exec(cn, ct,
                    @"INSERT dbo.NCLEX_REFERRAL_CODES (ReferralId,ReferralCode,ReferralCodeLimit,ReferralCodeUsed,CodeExpirationDate,DiscountGroupId,DiscountBehaviourTypeId,DiscountPercent)
                      VALUES (@id, @code, 50, 77, DATEADD(DAY,30,SYSUTCDATETIME()), 10, 1, 40)",
                    ("@id", id), ("@code", "SIM" + tag.ToUpper()));
                break;
            }
            case "blocked-ip":
            {
                await Exec(cn, ct,
                    @"INSERT dbo.USER_LOGS (UserId,SubscriptionId,IPAddress,CountryCode,DeviceTypeId,LogType,DateCreated)
                      VALUES (120, 5003, '45.66.77.88', 'US', 1, 'Access', SYSUTCDATETIME())");
                break;
            }
            default:
                throw new ArgumentException($"Unknown scenario '{scenario}'.");
        }

        var created = await _worker.RunOnceAsync(ct, requestId);
        return new SimulationResult(scenario, tag, created);
    }

    private static async Task Exec(SqlConnection cn, CancellationToken ct, string sql, params (string, object)[] ps)
    {
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> NextIdAsync(SqlConnection cn, string table, string column, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT ISNULL(MAX({column}), 0) + 1 FROM dbo.{table}", cn);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
