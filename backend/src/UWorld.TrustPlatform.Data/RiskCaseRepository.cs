using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public sealed record CaseFilter(string? RuleId = null, string? Severity = null, string? Status = null, string? AssignedTo = null);

public interface IRiskCaseRepository
{
    /// <summary>Idempotent: persists only if the CaseKey is new. Returns the new CaseId, or null if it already existed.</summary>
    Task<int?> SaveAsync(RiskCase riskCase, CancellationToken ct = default);
    Task<IReadOnlyList<RiskCase>> GetCasesAsync(CaseFilter filter, CancellationToken ct = default);
    Task<RiskCase?> GetCaseAsync(int caseId, CancellationToken ct = default);
    Task UpdateStatusAsync(int caseId, CaseStatus status, string? assignedTo, DateTime? resolvedUtc, CancellationToken ct = default);
    Task AddActivityAsync(int caseId, string action, string actor, string? notes, CancellationToken ct = default);
    Task<IReadOnlyList<DetectionRuleDefinition>> GetRuleDefinitionsAsync(CancellationToken ct = default);
}

public sealed class RiskCaseRepository : IRiskCaseRepository
{
    private readonly ISqlConnectionFactory _factory;

    public RiskCaseRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<int?> SaveAsync(RiskCase c, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = new SqlCommand("dbo.usp_UpsertRiskCase", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@CaseKey", c.CaseKey);
        cmd.Parameters.AddWithValue("@RuleId", c.RuleId);
        cmd.Parameters.AddWithValue("@RuleVersion", c.RuleVersion);
        cmd.Parameters.AddWithValue("@Title", c.Title);
        cmd.Parameters.AddWithValue("@Severity", c.Severity.ToString());
        cmd.Parameters.AddWithValue("@Confidence", c.Confidence);
        cmd.Parameters.AddWithValue("@Score", c.Score);
        cmd.Parameters.AddWithValue("@BusinessImpactUsd", c.BusinessImpactUsd);
        cmd.Parameters.AddWithValue("@ScoreBreakdownJson", (object?)JsonSerializer.Serialize(c.ScoreBreakdown) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AccountId", (object?)c.AccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@EntityRef", (object?)c.EntityRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExpectedJson", (object?)c.Expected ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ActualJson", (object?)c.Actual ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedUtc", c.CreatedUtc);
        var idParam = cmd.Parameters.Add("@CaseId", SqlDbType.Int); idParam.Direction = ParameterDirection.Output;
        var createdParam = cmd.Parameters.Add("@Created", SqlDbType.Bit); createdParam.Direction = ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var caseId = (int)idParam.Value;
        var created = (bool)createdParam.Value;
        if (!created) return null;   // already existed - stay idempotent, don't duplicate evidence

        foreach (var e in c.Evidence)
            await AddEvidenceAsync(cn, caseId, e, ct).ConfigureAwait(false);

        await AddActivityInternalAsync(cn, caseId, "Created", "system", $"Flagged by {c.RuleId}", ct).ConfigureAwait(false);
        return caseId;
    }

    private static async Task AddEvidenceAsync(SqlConnection cn, int caseId, CaseEvidence e, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.usp_AddCaseEvidence", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@CaseId", caseId);
        cmd.Parameters.AddWithValue("@SourceType", e.SourceType);
        cmd.Parameters.AddWithValue("@SourceId", e.SourceId);
        cmd.Parameters.AddWithValue("@Label", e.Label);
        cmd.Parameters.AddWithValue("@DetailJson", (object?)e.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OccurredUtc", (object?)e.OccurredUtc ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AddActivityAsync(int caseId, string action, string actor, string? notes, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await AddActivityInternalAsync(cn, caseId, action, actor, notes, ct).ConfigureAwait(false);
    }

    private static async Task AddActivityInternalAsync(SqlConnection cn, int caseId, string action, string actor, string? notes, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("dbo.usp_AddCaseActivity", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@CaseId", caseId);
        cmd.Parameters.AddWithValue("@Action", action);
        cmd.Parameters.AddWithValue("@Actor", actor);
        cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OccurredUtc", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(int caseId, CaseStatus status, string? assignedTo, DateTime? resolvedUtc, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("dbo.usp_UpdateCaseStatus", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@CaseId", caseId);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@AssignedTo", (object?)assignedTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ResolvedUtc", (object?)resolvedUtc ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RiskCase>> GetCasesAsync(CaseFilter filter, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT CaseId, CaseKey, RuleId, RuleVersion, Title, Severity, Confidence, Score, ScoreBreakdownJson,
                   Status, AccountId, EntityRef, ExpectedJson, ActualJson, AssignedTo, CreatedUtc, ResolvedUtc, BusinessImpactUsd
            FROM dbo.RiskCases
            WHERE (@RuleId     IS NULL OR RuleId     = @RuleId)
              AND (@Severity   IS NULL OR Severity   = @Severity)
              AND (@Status     IS NULL OR Status     = @Status)
              AND (@AssignedTo IS NULL OR AssignedTo = @AssignedTo)
            ORDER BY Score DESC, CreatedUtc DESC";

        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@RuleId", (object?)filter.RuleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Severity", (object?)filter.Severity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", (object?)filter.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AssignedTo", (object?)filter.AssignedTo ?? DBNull.Value);

        var cases = new List<RiskCase>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            cases.Add(MapCase(reader));
        return cases;
    }

    public async Task<RiskCase?> GetCaseAsync(int caseId, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);

        RiskCase? result;
        await using (var cmd = new SqlCommand(
            @"SELECT CaseId, CaseKey, RuleId, RuleVersion, Title, Severity, Confidence, Score, ScoreBreakdownJson,
                     Status, AccountId, EntityRef, ExpectedJson, ActualJson, AssignedTo, CreatedUtc, ResolvedUtc, BusinessImpactUsd
              FROM dbo.RiskCases WHERE CaseId = @CaseId", cn))
        {
            cmd.Parameters.AddWithValue("@CaseId", caseId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            result = MapCase(reader);
        }

        await using (var cmd = new SqlCommand(
            "SELECT EvidenceId, SourceType, SourceId, Label, DetailJson, OccurredUtc FROM dbo.CaseEvidence WHERE CaseId = @CaseId ORDER BY OccurredUtc", cn))
        {
            cmd.Parameters.AddWithValue("@CaseId", caseId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                result.Evidence.Add(new CaseEvidence
                {
                    EvidenceId = reader.GetInt32(0), CaseId = caseId,
                    SourceType = reader.GetString(1), SourceId = reader.GetString(2), Label = reader.GetString(3),
                    Detail = reader.IsDBNull(4) ? null : reader.GetString(4),
                    OccurredUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
                });
        }

        await using (var cmd = new SqlCommand(
            "SELECT ActivityId, Action, Actor, Notes, OccurredUtc FROM dbo.CaseActivity WHERE CaseId = @CaseId ORDER BY OccurredUtc", cn))
        {
            cmd.Parameters.AddWithValue("@CaseId", caseId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                result.Activity.Add(new CaseActivity
                {
                    ActivityId = reader.GetInt32(0), CaseId = caseId,
                    Action = reader.GetString(1), Actor = reader.GetString(2),
                    Notes = reader.IsDBNull(3) ? null : reader.GetString(3),
                    OccurredUtc = reader.GetDateTime(4)
                });
        }

        return result;
    }

    public async Task<IReadOnlyList<DetectionRuleDefinition>> GetRuleDefinitionsAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT RuleId, Name, Version, Category, Description, Enabled FROM dbo.RuleDefinitions ORDER BY Category, Name", cn);
        var rules = new List<DetectionRuleDefinition>();
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            rules.Add(new DetectionRuleDefinition(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetBoolean(5)));
        return rules;
    }

    private static RiskCase MapCase(SqlDataReader r) => new()
    {
        CaseId = r.GetInt32(0),
        CaseKey = r.GetString(1),
        RuleId = r.GetString(2),
        RuleVersion = r.GetString(3),
        Title = r.GetString(4),
        Severity = Enum.Parse<RiskSeverity>(r.GetString(5)),
        Confidence = r.GetDecimal(6),
        Score = r.GetDecimal(7),
        ScoreBreakdown = r.IsDBNull(8) ? new() : (JsonSerializer.Deserialize<List<ScoreFactor>>(r.GetString(8)) ?? new()),
        Status = Enum.Parse<CaseStatus>(r.GetString(9)),
        AccountId = r.IsDBNull(10) ? null : r.GetInt32(10),
        EntityRef = r.IsDBNull(11) ? null : r.GetString(11),
        Expected = r.IsDBNull(12) ? null : r.GetString(12),
        Actual = r.IsDBNull(13) ? null : r.GetString(13),
        AssignedTo = r.IsDBNull(14) ? null : r.GetString(14),
        CreatedUtc = r.GetDateTime(15),
        ResolvedUtc = r.IsDBNull(16) ? null : r.GetDateTime(16),
        BusinessImpactUsd = r.GetDecimal(17),
    };
}
