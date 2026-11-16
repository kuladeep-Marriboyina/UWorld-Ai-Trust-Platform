using System.Data;
using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public interface IUsageLedger
{
    Task AddAsync(LedgerEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<LedgerEntry>> GetAsync(string? requestId, int take, CancellationToken ct = default);
    Task<UsageSummary> GetSummaryAsync(CancellationToken ct = default);
}

public sealed class UsageLedgerRepository : IUsageLedger
{
    private readonly ISqlConnectionFactory _factory;

    public UsageLedgerRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(LedgerEntry e, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("dbo.usp_AddLedgerEntry", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@RequestId", e.RequestId);
        cmd.Parameters.AddWithValue("@Kind", e.Kind);
        cmd.Parameters.AddWithValue("@Operation", e.Operation);
        cmd.Parameters.AddWithValue("@CaseId", (object?)e.CaseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RuleId", (object?)e.RuleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Model", (object?)e.Model ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InputTokens", e.InputTokens);
        cmd.Parameters.AddWithValue("@OutputTokens", e.OutputTokens);
        cmd.Parameters.AddWithValue("@AiCostUsd", e.AiCostUsd);
        cmd.Parameters.AddWithValue("@BusinessImpactUsd", e.BusinessImpactUsd);
        cmd.Parameters.AddWithValue("@DurationMs", e.DurationMs);
        cmd.Parameters.AddWithValue("@OccurredUtc", e.OccurredUtc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetAsync(string? requestId, int take, CancellationToken ct = default)
    {
        var sql = $@"SELECT TOP (@Take) LedgerId, RequestId, Kind, Operation, CaseId, RuleId, Model,
                            InputTokens, OutputTokens, AiCostUsd, BusinessImpactUsd, DurationMs, OccurredUtc
                     FROM dbo.RequestLedger
                     WHERE (@RequestId IS NULL OR RequestId = @RequestId)
                     ORDER BY LedgerId DESC";
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Take", take);
        cmd.Parameters.AddWithValue("@RequestId", (object?)requestId ?? DBNull.Value);

        var rows = new List<LedgerEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new LedgerEntry
            {
                LedgerId = r.GetInt64(0), RequestId = r.GetString(1), Kind = r.GetString(2), Operation = r.GetString(3),
                CaseId = r.IsDBNull(4) ? null : r.GetInt32(4), RuleId = r.IsDBNull(5) ? null : r.GetString(5),
                Model = r.IsDBNull(6) ? null : r.GetString(6), InputTokens = r.GetInt32(7), OutputTokens = r.GetInt32(8),
                AiCostUsd = r.GetDecimal(9), BusinessImpactUsd = r.GetDecimal(10), DurationMs = r.GetInt32(11),
                OccurredUtc = r.GetDateTime(12)
            });
        return rows;
    }

    public async Task<UsageSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);

        int requests = 0, aiInvestigations = 0;
        long inTok = 0, outTok = 0;
        decimal aiCost = 0, impact = 0;
        await using (var cmd = new SqlCommand(
            @"SELECT
                COUNT(DISTINCT RequestId),
                SUM(CASE WHEN Kind='AiInvestigation' THEN 1 ELSE 0 END),
                SUM(CAST(InputTokens AS BIGINT)), SUM(CAST(OutputTokens AS BIGINT)),
                SUM(AiCostUsd), SUM(BusinessImpactUsd)
              FROM dbo.RequestLedger", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                requests = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                aiInvestigations = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                inTok = r.IsDBNull(2) ? 0 : r.GetInt64(2);
                outTok = r.IsDBNull(3) ? 0 : r.GetInt64(3);
                aiCost = r.IsDBNull(4) ? 0 : r.GetDecimal(4);
                impact = r.IsDBNull(5) ? 0 : r.GetDecimal(5);
            }
        }

        var byRule = new List<RuleCost>();
        await using (var cmd = new SqlCommand(
            @"SELECT RuleId,
                     SUM(CASE WHEN Kind='CaseRaised' THEN 1 ELSE 0 END) AS Cases,
                     SUM(AiCostUsd), SUM(BusinessImpactUsd)
              FROM dbo.RequestLedger
              WHERE RuleId IS NOT NULL
              GROUP BY RuleId
              ORDER BY SUM(BusinessImpactUsd) DESC", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                byRule.Add(new RuleCost(r.GetString(0), r.GetInt32(1), r.GetDecimal(2), r.GetDecimal(3)));
        }

        return new UsageSummary(requests, aiInvestigations, inTok, outTok, aiCost, impact, byRule);
    }
}
