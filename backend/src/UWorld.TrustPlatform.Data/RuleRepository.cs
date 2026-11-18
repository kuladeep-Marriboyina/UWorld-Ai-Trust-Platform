using System.Text.Json;
using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public sealed record RuleStats(string RuleId, int Total, int Open, int Confirmed, int FalsePositive, int Benign, DateTime? LastFiredUtc, decimal OpenAtRiskUsd, decimal ConfirmedUsd, int Last24h);

public interface IRuleRepository
{
    Task<IReadOnlyList<RuleConfigEntry>> GetConfigsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RuleVersion>> GetVersionsAsync(string ruleId, CancellationToken ct = default);
    Task<RuleVersion?> GetVersionAsync(string ruleId, int versionNo, CancellationToken ct = default);
    Task<IReadOnlyList<RuleVersion>> GetPendingAsync(CancellationToken ct = default);
    Task<int> ProposeAsync(RuleDefinitionDraft draft, bool isDeclarative, string author, CancellationToken ct = default);
    Task ApproveAsync(string ruleId, int versionNo, string reviewer, string? note, CancellationToken ct = default);
    Task RejectAsync(string ruleId, int versionNo, string reviewer, string? note, CancellationToken ct = default);
    Task SetEnabledAsync(string ruleId, bool enabled, string actor, string? note, CancellationToken ct = default);
    Task<IReadOnlyList<RuleStats>> GetStatsAsync(CancellationToken ct = default);
}

public sealed class RuleRepository : IRuleRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ISqlConnectionFactory _factory;
    public RuleRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<RuleConfigEntry>> GetConfigsAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"SELECT RuleId, Name, Category, Severity, Enabled, ConfigJson, IsDeclarative, DeclarativeJson, Author, Version, Description,
                Status, Owner, VersionNo, ApprovedBy, ApprovedUtc, Rationale, PlaybookJson, PendingVersionNo, CreatedUtc, DateUpdated
            FROM dbo.RuleDefinitions ORDER BY Category, Name", cn);
        var list = new List<RuleConfigEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RuleConfigEntry
            {
                RuleId = r.GetString(0), Name = S(1), Category = S(2), Severity = S(3), Enabled = r.GetBoolean(4),
                ConfigJson = S(5), IsDeclarative = !r.IsDBNull(6) && r.GetBoolean(6), DeclarativeJson = S(7), Author = S(8),
                Version = S(9), Description = S(10), Status = S(11) ?? RuleStatus.Active, Owner = S(12), VersionNo = r.GetInt32(13),
                ApprovedBy = S(14), ApprovedUtc = r.IsDBNull(15) ? null : r.GetDateTime(15), Rationale = S(16), PlaybookJson = S(17),
                PendingVersionNo = r.IsDBNull(18) ? null : r.GetInt32(18), CreatedUtc = r.IsDBNull(19) ? null : r.GetDateTime(19),
                DateUpdated = r.IsDBNull(20) ? null : r.GetDateTime(20),
            });
        return list;
    }

    private const string VersionCols = "Id, RuleId, VersionNo, Status, DefinitionJson, ChangeNote, Author, Reviewer, ReviewNote, CreatedUtc, ReviewedUtc";

    private static RuleVersion MapVersion(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0), RuleId = r.GetString(1), VersionNo = r.GetInt32(2), Status = r.GetString(3), DefinitionJson = r.GetString(4),
        ChangeNote = r.IsDBNull(5) ? null : r.GetString(5), Author = r.GetString(6), Reviewer = r.IsDBNull(7) ? null : r.GetString(7),
        ReviewNote = r.IsDBNull(8) ? null : r.GetString(8), CreatedUtc = r.GetDateTime(9), ReviewedUtc = r.IsDBNull(10) ? null : r.GetDateTime(10)
    };

    public async Task<IReadOnlyList<RuleVersion>> GetVersionsAsync(string ruleId, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand($"SELECT {VersionCols} FROM dbo.RuleVersions WHERE RuleId=@id ORDER BY Id DESC", cn);
        cmd.Parameters.AddWithValue("@id", ruleId);
        var list = new List<RuleVersion>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(MapVersion(r));
        return list;
    }

    public async Task<RuleVersion?> GetVersionAsync(string ruleId, int versionNo, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand($"SELECT TOP 1 {VersionCols} FROM dbo.RuleVersions WHERE RuleId=@id AND VersionNo=@v AND Status NOT IN ('Enabled','Disabled') ORDER BY Id DESC", cn);
        cmd.Parameters.AddWithValue("@id", ruleId);
        cmd.Parameters.AddWithValue("@v", versionNo);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await r.ReadAsync(ct).ConfigureAwait(false) ? MapVersion(r) : null;
    }

    public async Task<IReadOnlyList<RuleVersion>> GetPendingAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand($"SELECT {VersionCols} FROM dbo.RuleVersions WHERE Status='PendingApproval' ORDER BY CreatedUtc", cn);
        var list = new List<RuleVersion>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(MapVersion(r));
        return list;
    }

    public async Task<int> ProposeAsync(RuleDefinitionDraft d, bool isDeclarative, string author, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM dbo.RuleDefinitions WHERE RuleId=@id)
    INSERT dbo.RuleDefinitions (RuleId, Name, Version, Category, Description, Severity, Enabled, IsDeclarative, Author, Status, Owner, VersionNo, CreatedUtc, DateUpdated, Rationale)
    VALUES (@id, @name, 'authored', @cat, @desc, @sev, 0, @decl, @author, 'PendingApproval', @author, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), @why);
UPDATE dbo.RuleVersions SET Status='Superseded', ReviewNote='Replaced by a newer proposal', ReviewedUtc=SYSUTCDATETIME()
 WHERE RuleId=@id AND Status='PendingApproval';", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", d.RuleId);
            cmd.Parameters.AddWithValue("@name", d.Name);
            cmd.Parameters.AddWithValue("@cat", d.Category);
            cmd.Parameters.AddWithValue("@desc", d.Description);
            cmd.Parameters.AddWithValue("@sev", d.Severity);
            cmd.Parameters.AddWithValue("@decl", isDeclarative);
            cmd.Parameters.AddWithValue("@author", author);
            cmd.Parameters.AddWithValue("@why", (object?)d.Rationale ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        int next;
        await using (var cmd = new SqlCommand("SELECT ISNULL(MAX(VersionNo), 0) + 1 FROM dbo.RuleVersions WHERE RuleId=@id", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", d.RuleId);
            next = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        await using (var cmd = new SqlCommand(@"
INSERT dbo.RuleVersions (RuleId, VersionNo, Status, DefinitionJson, ChangeNote, Author, CreatedUtc)
VALUES (@id, @v, 'PendingApproval', @json, @note, @author, SYSUTCDATETIME());
UPDATE dbo.RuleDefinitions SET PendingVersionNo=@v, DateUpdated=SYSUTCDATETIME() WHERE RuleId=@id;", cn, tx))
        {
            cmd.Parameters.AddWithValue("@id", d.RuleId);
            cmd.Parameters.AddWithValue("@v", next);
            cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(d, Json));
            cmd.Parameters.AddWithValue("@note", (object?)d.ChangeNote ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@author", author);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return next;
    }

    public async Task ApproveAsync(string ruleId, int versionNo, string reviewer, string? note, CancellationToken ct = default)
    {
        var v = await GetVersionAsync(ruleId, versionNo, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Version not found.");
        if (v.Status != "PendingApproval") throw new InvalidOperationException($"Version {versionNo} is {v.Status}, not pending approval.");
        var d = JsonSerializer.Deserialize<RuleDefinitionDraft>(v.DefinitionJson, Json) ?? throw new InvalidOperationException("Corrupt definition.");

        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
UPDATE dbo.RuleVersions SET Status='Superseded' WHERE RuleId=@id AND Status='Approved';
UPDATE dbo.RuleVersions SET Status='Approved', Reviewer=@rev, ReviewNote=@note, ReviewedUtc=SYSUTCDATETIME() WHERE Id=@vid;
UPDATE dbo.RuleDefinitions SET Name=@name, Category=@cat, Severity=@sev, Description=@desc, Rationale=@why,
    ConfigJson = CASE WHEN @cfg IS NULL THEN ConfigJson ELSE @cfg END,
    DeclarativeJson = CASE WHEN @logic IS NULL THEN DeclarativeJson ELSE @logic END,
    PlaybookJson=@pb, VersionNo=@v, Status='Active', Enabled=1, ApprovedBy=@rev, ApprovedUtc=SYSUTCDATETIME(),
    PendingVersionNo=NULL, DateUpdated=SYSUTCDATETIME(), Author=@author
WHERE RuleId=@id;", cn);
        cmd.Parameters.AddWithValue("@id", ruleId);
        cmd.Parameters.AddWithValue("@vid", v.Id);
        cmd.Parameters.AddWithValue("@v", versionNo);
        cmd.Parameters.AddWithValue("@rev", reviewer);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@name", d.Name);
        cmd.Parameters.AddWithValue("@cat", d.Category);
        cmd.Parameters.AddWithValue("@sev", d.Severity);
        cmd.Parameters.AddWithValue("@desc", d.Description);
        cmd.Parameters.AddWithValue("@why", (object?)d.Rationale ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cfg", (object?)d.ConfigJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@logic", d.Logic is null ? DBNull.Value : JsonSerializer.Serialize(d.Logic));
        cmd.Parameters.AddWithValue("@pb", JsonSerializer.Serialize(d.Playbook));
        cmd.Parameters.AddWithValue("@author", v.Author);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RejectAsync(string ruleId, int versionNo, string reviewer, string? note, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
UPDATE dbo.RuleVersions SET Status='Rejected', Reviewer=@rev, ReviewNote=@note, ReviewedUtc=SYSUTCDATETIME()
 WHERE RuleId=@id AND VersionNo=@v AND Status='PendingApproval';
UPDATE dbo.RuleDefinitions SET PendingVersionNo=NULL, DateUpdated=SYSUTCDATETIME(),
    Status = CASE WHEN Status='PendingApproval' THEN 'Rejected' ELSE Status END
 WHERE RuleId=@id;", cn);
        cmd.Parameters.AddWithValue("@id", ruleId);
        cmd.Parameters.AddWithValue("@v", versionNo);
        cmd.Parameters.AddWithValue("@rev", reviewer);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(string ruleId, bool enabled, string actor, string? note, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
UPDATE dbo.RuleDefinitions SET Enabled=@e, DateUpdated=SYSUTCDATETIME(),
    Status = CASE WHEN @e = 1 AND Status = 'Disabled' THEN 'Active' WHEN @e = 0 AND Status = 'Active' THEN 'Disabled' ELSE Status END
 WHERE RuleId=@id;
INSERT dbo.RuleVersions (RuleId, VersionNo, Status, DefinitionJson, ChangeNote, Author, Reviewer, CreatedUtc, ReviewedUtc)
SELECT RuleId, VersionNo, CASE WHEN @e = 1 THEN 'Enabled' ELSE 'Disabled' END, '{}', @note, @who, @who, SYSUTCDATETIME(), SYSUTCDATETIME()
  FROM dbo.RuleDefinitions WHERE RuleId=@id;", cn);
        cmd.Parameters.AddWithValue("@e", enabled);
        cmd.Parameters.AddWithValue("@id", ruleId);
        cmd.Parameters.AddWithValue("@who", actor);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? (enabled ? "Rule enabled" : "Rule disabled"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RuleStats>> GetStatsAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT RuleId, COUNT(*),
                   SUM(CASE WHEN Status <> 'Resolved' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Disposition = 'ConfirmedFraud' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Disposition = 'FalsePositive' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN Disposition = 'Benign' THEN 1 ELSE 0 END),
                   MAX(CreatedUtc),
                   SUM(CASE WHEN Status <> 'Resolved' THEN BusinessImpactUsd ELSE 0 END),
                   SUM(CASE WHEN Disposition = 'ConfirmedFraud' THEN BusinessImpactUsd ELSE 0 END),
                   SUM(CASE WHEN CreatedUtc >= DATEADD(HOUR, -24, SYSUTCDATETIME()) THEN 1 ELSE 0 END)
            FROM dbo.RiskCases GROUP BY RuleId";
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, cn);
        var list = new List<RuleStats>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RuleStats(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5),
                r.IsDBNull(6) ? null : r.GetDateTime(6), r.GetDecimal(7), r.GetDecimal(8), r.GetInt32(9)));
        return list;
    }
}
