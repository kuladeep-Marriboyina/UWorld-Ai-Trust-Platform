using System.Text.Json;
using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public sealed record CaseFilter(string? RuleId = null, string? Severity = null, string? Status = null, string? AssignedTo = null,
    string? Category = null, string? Query = null, bool? OpenOnly = null, int Take = 500);

public sealed record RelatedCase(int CaseId, string Title, string RuleId, string Severity, string Status, DateTime CreatedUtc, string SharedType, string SharedValue);

public interface IRiskCaseRepository
{
    /// <summary>Idempotent by CaseKey. A known case that gained evidence is updated (or reopened) rather than duplicated.</summary>
    Task<CaseSaveOutcome> SaveAsync(RiskCase riskCase, CancellationToken ct = default);
    Task<IReadOnlyList<RiskCase>> GetCasesAsync(CaseFilter filter, CancellationToken ct = default);
    Task<RiskCase?> GetCaseAsync(int caseId, CancellationToken ct = default);
    Task UpdateStatusAsync(int caseId, CaseStatus status, string? assignedTo, DateTime? resolvedUtc, CancellationToken ct = default);
    Task AddActivityAsync(int caseId, string action, string actor, string? notes, CancellationToken ct = default);
    Task AcknowledgeAsync(int caseId, string actor, CancellationToken ct = default);
    Task SetDispositionAsync(int caseId, string disposition, string actor, CancellationToken ct = default);
    Task<IReadOnlyList<RelatedCase>> GetRelatedAsync(int caseId, CancellationToken ct = default);
    Task<string?> GetAiSummaryAsync(int caseId, CancellationToken ct = default);
    Task SaveAiSummaryAsync(int caseId, string json, CancellationToken ct = default);
    Task<IReadOnlyList<DetectionRuleDefinition>> GetRuleDefinitionsAsync(CancellationToken ct = default);
}

public sealed class RiskCaseRepository : IRiskCaseRepository
{
    private const string Columns = @"c.CaseId, c.CaseKey, c.RuleId, c.RuleVersion, c.Title, c.Severity, c.Confidence, c.Score, c.ScoreBreakdownJson,
        c.Status, c.AccountId, c.EntityRef, c.ExpectedJson, c.ActualJson, c.AssignedTo, c.CreatedUtc, c.ResolvedUtc, c.BusinessImpactUsd,
        c.Category, c.Disposition, c.DispositionBy, c.FirstSignalUtc, c.TriggerSignalUtc, c.UpdatedUtc, c.AcknowledgedUtc, c.AcknowledgedBy,
        c.DetectionLatencyMs, c.ImpactFormula, c.PlaybookJson,
        (SELECT COUNT(*) FROM dbo.CaseEvidence e WHERE e.CaseId = c.CaseId) AS EvidenceCount";

    private readonly ISqlConnectionFactory _factory;
    public RiskCaseRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<CaseSaveOutcome> SaveAsync(RiskCase c, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);

        int? existingId = null; string? status = null; decimal oldScore = 0; string? oldSev = null; string? disposition = null;
        await using (var cmd = new SqlCommand("SELECT CaseId, Status, Score, Severity, Disposition FROM dbo.RiskCases WHERE CaseKey = @k", cn))
        {
            cmd.Parameters.AddWithValue("@k", c.CaseKey);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                existingId = r.GetInt32(0); status = r.GetString(1); oldScore = r.GetDecimal(2); oldSev = r.GetString(3);
                disposition = r.IsDBNull(4) ? null : r.GetString(4);
            }
        }

        var now = DateTime.UtcNow;
        if (existingId is null)
        {
            c.CreatedUtc = now;
            c.DetectionLatencyMs = c.TriggerSignalUtc is DateTime t && now - t < TimeSpan.FromMinutes(10) && now >= t
                ? (int)(now - t).TotalMilliseconds : null;

            int id;
            await using (var cmd = new SqlCommand(@"
INSERT dbo.RiskCases (CaseKey, RuleId, RuleVersion, Title, Severity, Confidence, Score, BusinessImpactUsd, ScoreBreakdownJson, Status,
    AccountId, EntityRef, ExpectedJson, ActualJson, CreatedUtc, Category, FirstSignalUtc, TriggerSignalUtc, UpdatedUtc, DetectionLatencyMs, ImpactFormula, PlaybookJson)
OUTPUT INSERTED.CaseId
VALUES (@key, @rule, @ver, @title, @sev, @conf, @score, @impact, @breakdown, 'New',
    @acct, @ref, @exp, @act, @created, @cat, @first, @trigger, @created, @latency, @formula, @playbook)", cn))
            {
                cmd.Parameters.AddWithValue("@key", c.CaseKey);
                cmd.Parameters.AddWithValue("@rule", c.RuleId);
                cmd.Parameters.AddWithValue("@ver", c.RuleVersion);
                AddCommon(cmd, c);
                cmd.Parameters.AddWithValue("@acct", (object?)c.AccountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ref", (object?)c.EntityRef ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@exp", (object?)c.Expected ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@created", now);
                cmd.Parameters.AddWithValue("@cat", (object?)c.Category ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@first", (object?)c.FirstSignalUtc ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@latency", (object?)c.DetectionLatencyMs ?? DBNull.Value);
                id = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            }
            foreach (var e in c.Evidence) await AddEvidenceAsync(cn, id, e, ct).ConfigureAwait(false);
            foreach (var e in c.Entities.DistinctBy(x => (x.Type, x.Value))) await AddEntityAsync(cn, id, e, ct).ConfigureAwait(false);
            var latency = c.DetectionLatencyMs is int ms ? $" · detected {Durations.Short(ms)} after the triggering event" : " · found on backfill";
            await AddActivityInternalAsync(cn, id, "Created", "system", $"Raised by {c.RuleId} ({c.RuleVersion}) at score {c.Score:N0}{latency}", now, ct).ConfigureAwait(false);
            c.CaseId = id; c.Status = CaseStatus.New;
            return new CaseSaveOutcome(id, "Created", null, c);
        }

        var caseId = existingId.Value;
        // an analyst has ruled this out — stay quiet unless the case is genuinely new
        if (status == "Resolved" && disposition is Dispositions.FalsePositive or Dispositions.Benign)
            return new CaseSaveOutcome(caseId, "Unchanged", null, c);

        var known = new HashSet<string>();
        await using (var cmd = new SqlCommand("SELECT SourceType + '|' + SourceId FROM dbo.CaseEvidence WHERE CaseId = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", caseId);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) known.Add(r.GetString(0));
        }
        var fresh = c.Evidence.Where(e => !known.Contains($"{e.SourceType}|{e.SourceId}")).ToList();
        if (fresh.Count == 0 && Math.Abs(oldScore - c.Score) < 0.5m)
            return new CaseSaveOutcome(caseId, "Unchanged", null, c);

        var reopen = status == "Resolved";
        await using (var cmd = new SqlCommand(@"
UPDATE dbo.RiskCases SET Title=@title, Severity=@sev, Confidence=@conf, Score=@score, BusinessImpactUsd=@impact, ScoreBreakdownJson=@breakdown,
    ActualJson=@act, TriggerSignalUtc=@trigger, UpdatedUtc=@now, ImpactFormula=@formula, PlaybookJson=@playbook,
    Status = CASE WHEN Status = 'Resolved' THEN 'New' ELSE Status END,
    ResolvedUtc = CASE WHEN Status = 'Resolved' THEN NULL ELSE ResolvedUtc END
WHERE CaseId=@id", cn))
        {
            AddCommon(cmd, c);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@id", caseId);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        foreach (var e in fresh) await AddEvidenceAsync(cn, caseId, e, ct).ConfigureAwait(false);

        var knownEntities = new HashSet<string>();
        await using (var cmd = new SqlCommand("SELECT EntityType + '|' + EntityValue FROM dbo.CaseEntities WHERE CaseId = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", caseId);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) knownEntities.Add(r.GetString(0));
        }
        foreach (var e in c.Entities.DistinctBy(x => (x.Type, x.Value)).Where(x => !knownEntities.Contains($"{x.Type}|{x.Value}")))
            await AddEntityAsync(cn, caseId, e, ct).ConfigureAwait(false);

        var sevChange = oldSev != c.Severity.ToString() ? $" ({oldSev} → {c.Severity})" : "";
        var change = (Math.Round(oldScore) == Math.Round(c.Score) && sevChange.Length == 0 ? $"Score unchanged at {c.Score:N0}" : $"Score {oldScore:N0} → {c.Score:N0}{sevChange}") +
                     (fresh.Count > 0 ? $" · {fresh.Count} new evidence: {string.Join("; ", fresh.Take(3).Select(e => e.Label))}{(fresh.Count > 3 ? "…" : "")}" : "");
        await AddActivityInternalAsync(cn, caseId, reopen ? "Reopened" : "Updated", "system",
            (reopen ? "New evidence after resolution. " : "") + change, now, ct).ConfigureAwait(false);
        c.CaseId = caseId;
        return new CaseSaveOutcome(caseId, reopen ? "Reopened" : "Updated", change, c);
    }

    private static void AddCommon(SqlCommand cmd, RiskCase c)
    {
        cmd.Parameters.AddWithValue("@title", c.Title);
        cmd.Parameters.AddWithValue("@sev", c.Severity.ToString());
        cmd.Parameters.AddWithValue("@conf", c.Confidence);
        cmd.Parameters.AddWithValue("@score", c.Score);
        cmd.Parameters.AddWithValue("@impact", c.BusinessImpactUsd);
        cmd.Parameters.AddWithValue("@breakdown", JsonSerializer.Serialize(c.ScoreBreakdown));
        cmd.Parameters.AddWithValue("@act", (object?)c.Actual ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@trigger", (object?)c.TriggerSignalUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@formula", (object?)c.ImpactFormula ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@playbook", JsonSerializer.Serialize(c.Playbook));
    }

    private static async Task AddEvidenceAsync(SqlConnection cn, int caseId, CaseEvidence e, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"INSERT dbo.CaseEvidence (CaseId, SourceType, SourceId, Label, DetailJson, OccurredUtc)
                                               VALUES (@c, @t, @s, @l, @d, @o)", cn);
        cmd.Parameters.AddWithValue("@c", caseId);
        cmd.Parameters.AddWithValue("@t", e.SourceType);
        cmd.Parameters.AddWithValue("@s", e.SourceId);
        cmd.Parameters.AddWithValue("@l", e.Label.Length > 256 ? e.Label[..256] : e.Label);
        cmd.Parameters.AddWithValue("@d", (object?)e.Detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", (object?)e.OccurredUtc ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task AddEntityAsync(SqlConnection cn, int caseId, CaseEntity e, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("INSERT dbo.CaseEntities (CaseId, EntityType, EntityValue) VALUES (@c, @t, @v)", cn);
        cmd.Parameters.AddWithValue("@c", caseId);
        cmd.Parameters.AddWithValue("@t", e.Type);
        cmd.Parameters.AddWithValue("@v", e.Value.Length > 256 ? e.Value[..256] : e.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AddActivityAsync(int caseId, string action, string actor, string? notes, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await AddActivityInternalAsync(cn, caseId, action, actor, notes, DateTime.UtcNow, ct).ConfigureAwait(false);
    }

    private static async Task AddActivityInternalAsync(SqlConnection cn, int caseId, string action, string actor, string? notes, DateTime when, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("INSERT dbo.CaseActivity (CaseId, Action, Actor, Notes, OccurredUtc) VALUES (@c, @a, @who, @n, @o)", cn);
        cmd.Parameters.AddWithValue("@c", caseId);
        cmd.Parameters.AddWithValue("@a", action);
        cmd.Parameters.AddWithValue("@who", actor);
        cmd.Parameters.AddWithValue("@n", (object?)(notes is { Length: > 2000 } ? notes[..2000] : notes) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", when);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(int caseId, CaseStatus status, string? assignedTo, DateTime? resolvedUtc, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"UPDATE dbo.RiskCases SET Status=@s, AssignedTo=COALESCE(@a, AssignedTo), ResolvedUtc=@r, UpdatedUtc=SYSUTCDATETIME(),
                AcknowledgedUtc = COALESCE(AcknowledgedUtc, CASE WHEN @s <> 'New' THEN SYSUTCDATETIME() END),
                AcknowledgedBy  = COALESCE(AcknowledgedBy, CASE WHEN @s <> 'New' THEN COALESCE(@a, 'system') END)
            WHERE CaseId=@id", cn);
        cmd.Parameters.AddWithValue("@s", status.ToString());
        cmd.Parameters.AddWithValue("@a", (object?)assignedTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@r", (object?)resolvedUtc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", caseId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AcknowledgeAsync(int caseId, string actor, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("UPDATE dbo.RiskCases SET AcknowledgedUtc = COALESCE(AcknowledgedUtc, SYSUTCDATETIME()), AcknowledgedBy = COALESCE(AcknowledgedBy, @a) WHERE CaseId=@id", cn);
        cmd.Parameters.AddWithValue("@a", actor);
        cmd.Parameters.AddWithValue("@id", caseId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetDispositionAsync(int caseId, string disposition, string actor, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("UPDATE dbo.RiskCases SET Disposition=@d, DispositionBy=@a, UpdatedUtc=SYSUTCDATETIME() WHERE CaseId=@id", cn);
        cmd.Parameters.AddWithValue("@d", disposition);
        cmd.Parameters.AddWithValue("@a", actor);
        cmd.Parameters.AddWithValue("@id", caseId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RiskCase>> GetCasesAsync(CaseFilter f, CancellationToken ct = default)
    {
        var sql = $@"
            SELECT TOP (@take) {Columns}
            FROM dbo.RiskCases c
            WHERE (@RuleId     IS NULL OR c.RuleId     = @RuleId)
              AND (@Severity   IS NULL OR c.Severity   = @Severity)
              AND (@Status     IS NULL OR c.Status     = @Status)
              AND (@AssignedTo IS NULL OR c.AssignedTo = @AssignedTo)
              AND (@Category   IS NULL OR c.Category   = @Category)
              AND (@Open = 0 OR c.Status <> 'Resolved')
              AND (@Q IS NULL OR c.Title LIKE @Q OR CAST(c.CaseId AS NVARCHAR(12)) = @QRaw
                   OR EXISTS (SELECT 1 FROM dbo.CaseEntities x WHERE x.CaseId = c.CaseId AND x.EntityValue LIKE @Q))
            ORDER BY CASE WHEN c.Status = 'Resolved' THEN 1 ELSE 0 END,
                     CASE c.Severity WHEN 'Critical' THEN 0 WHEN 'High' THEN 1 WHEN 'Medium' THEN 2 ELSE 3 END,
                     COALESCE(c.UpdatedUtc, c.CreatedUtc) DESC";

        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@take", f.Take);
        cmd.Parameters.AddWithValue("@RuleId", (object?)f.RuleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Severity", (object?)f.Severity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", (object?)f.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AssignedTo", (object?)f.AssignedTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Category", (object?)f.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Open", f.OpenOnly == true ? 1 : 0);
        cmd.Parameters.AddWithValue("@Q", string.IsNullOrWhiteSpace(f.Query) ? DBNull.Value : $"%{f.Query.Trim()}%");
        cmd.Parameters.AddWithValue("@QRaw", string.IsNullOrWhiteSpace(f.Query) ? DBNull.Value : f.Query.Trim().TrimStart('#'));

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
        await using (var cmd = new SqlCommand($"SELECT {Columns} FROM dbo.RiskCases c WHERE c.CaseId = @CaseId", cn))
        {
            cmd.Parameters.AddWithValue("@CaseId", caseId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
            result = MapCase(reader);
        }

        await using (var cmd = new SqlCommand(
            "SELECT EvidenceId, SourceType, SourceId, Label, DetailJson, OccurredUtc FROM dbo.CaseEvidence WHERE CaseId = @CaseId ORDER BY OccurredUtc, EvidenceId", cn))
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
            "SELECT ActivityId, Action, Actor, Notes, OccurredUtc FROM dbo.CaseActivity WHERE CaseId = @CaseId ORDER BY OccurredUtc, ActivityId", cn))
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

        await using (var cmd = new SqlCommand("SELECT EntityType, EntityValue FROM dbo.CaseEntities WHERE CaseId = @CaseId ORDER BY Id", cn))
        {
            cmd.Parameters.AddWithValue("@CaseId", caseId);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                result.Entities.Add(new CaseEntity(reader.GetString(0), reader.GetString(1)));
        }
        return result;
    }

    public async Task<IReadOnlyList<RelatedCase>> GetRelatedAsync(int caseId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT DISTINCT TOP 20 c.CaseId, c.Title, c.RuleId, c.Severity, c.Status, c.CreatedUtc, o.EntityType, o.EntityValue
            FROM dbo.CaseEntities mine
            JOIN dbo.CaseEntities o ON o.EntityType = mine.EntityType AND o.EntityValue = mine.EntityValue AND o.CaseId <> mine.CaseId
            JOIN dbo.RiskCases c ON c.CaseId = o.CaseId
            WHERE mine.CaseId = @id AND mine.EntityType IN ('ip','device','card','user','email','code','domain','subscription')
            ORDER BY c.CreatedUtc DESC";
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@id", caseId);
        var list = new List<RelatedCase>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RelatedCase(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDateTime(5), r.GetString(6), r.GetString(7)));
        return list;
    }

    public async Task<string?> GetAiSummaryAsync(int caseId, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT AiSummaryJson FROM dbo.RiskCases WHERE CaseId=@id", cn);
        cmd.Parameters.AddWithValue("@id", caseId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    public async Task SaveAiSummaryAsync(int caseId, string json, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("UPDATE dbo.RiskCases SET AiSummaryJson=@j WHERE CaseId=@id", cn);
        cmd.Parameters.AddWithValue("@j", json);
        cmd.Parameters.AddWithValue("@id", caseId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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

    private static RiskCase MapCase(SqlDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        DateTime? D(int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
        var c = new RiskCase
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
            EntityRef = S(11),
            Expected = S(12),
            Actual = S(13),
            AssignedTo = S(14),
            CreatedUtc = r.GetDateTime(15),
            ResolvedUtc = D(16),
            BusinessImpactUsd = r.GetDecimal(17),
            Category = S(18),
            Disposition = S(19),
            DispositionBy = S(20),
            FirstSignalUtc = D(21),
            TriggerSignalUtc = D(22),
            UpdatedUtc = D(23),
            AcknowledgedUtc = D(24),
            AcknowledgedBy = S(25),
            DetectionLatencyMs = r.IsDBNull(26) ? null : r.GetInt32(26),
            ImpactFormula = S(27),
            EvidenceCount = r.GetInt32(29),
        };
        if (S(28) is { } pb) { try { c.Playbook = JsonSerializer.Deserialize<List<string>>(pb) ?? new(); } catch { } }
        return c;
    }
}
