using System.Text.Json;
using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public interface IRuleRepository
{
    Task<IReadOnlyList<RuleConfigEntry>> GetConfigsAsync(CancellationToken ct = default);
    Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct = default);
    Task UpsertDeclarativeAsync(DeclarativeRule rule, CancellationToken ct = default);
}

public sealed class RuleRepository : IRuleRepository
{
    private readonly ISqlConnectionFactory _factory;
    public RuleRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<RuleConfigEntry>> GetConfigsAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT RuleId, Name, Category, Severity, Enabled, ConfigJson, IsDeclarative, DeclarativeJson, Author, Version, Description FROM dbo.RuleDefinitions", cn);
        var list = new List<RuleConfigEntry>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new RuleConfigEntry
            {
                RuleId = r.GetString(0),
                Name = r.IsDBNull(1) ? null : r.GetString(1),
                Category = r.IsDBNull(2) ? null : r.GetString(2),
                Severity = r.IsDBNull(3) ? null : r.GetString(3),
                Enabled = r.GetBoolean(4),
                ConfigJson = r.IsDBNull(5) ? null : r.GetString(5),
                IsDeclarative = !r.IsDBNull(6) && r.GetBoolean(6),
                DeclarativeJson = r.IsDBNull(7) ? null : r.GetString(7),
                Author = r.IsDBNull(8) ? null : r.GetString(8),
                Version = r.IsDBNull(9) ? null : r.GetString(9),
                Description = r.IsDBNull(10) ? null : r.GetString(10),
            });
        return list;
    }

    public async Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "UPDATE dbo.RuleDefinitions SET Enabled=@e, DateUpdated=SYSUTCDATETIME() WHERE RuleId=@id", cn);
        cmd.Parameters.AddWithValue("@e", enabled);
        cmd.Parameters.AddWithValue("@id", ruleId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertDeclarativeAsync(DeclarativeRule rule, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(rule);
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
MERGE dbo.RuleDefinitions AS t
USING (SELECT @id AS RuleId) AS s ON t.RuleId = s.RuleId
WHEN MATCHED THEN UPDATE SET Name=@name, Version='nl-v1', Category=@cat, Description=@desc,
    Severity=@sev, Enabled=1, IsDeclarative=1, DeclarativeJson=@json, Author=@author, DateUpdated=SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (RuleId, Name, Version, Category, Description, Severity, Enabled, IsDeclarative, DeclarativeJson, Author, DateUpdated)
    VALUES (@id, @name, 'nl-v1', @cat, @desc, @sev, 1, 1, @json, @author, SYSUTCDATETIME());", cn);
        cmd.Parameters.AddWithValue("@id", rule.RuleId);
        cmd.Parameters.AddWithValue("@name", rule.Name);
        cmd.Parameters.AddWithValue("@cat", rule.Category);
        cmd.Parameters.AddWithValue("@desc", rule.EnglishSource);
        cmd.Parameters.AddWithValue("@sev", rule.Severity);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@author", rule.Author);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
