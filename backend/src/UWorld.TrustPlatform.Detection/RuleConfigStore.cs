using System.Text.Json;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

/// <summary>
/// Runtime rule configuration loaded from RULE_DEFINITIONS. Lets rules read thresholds and lets
/// the engine skip disabled rules — so behaviour changes (enable/disable, threshold edits,
/// NL-authored declarative rules) take effect without a redeploy.
/// </summary>
public sealed class RuleConfigStore
{
    private volatile Dictionary<string, RuleConfigEntry> _map = new(StringComparer.OrdinalIgnoreCase);

    public void Reload(IEnumerable<RuleConfigEntry> entries) =>
        _map = entries.ToDictionary(e => e.RuleId, e => e, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<RuleConfigEntry> All => _map.Values;
    public RuleConfigEntry? Get(string ruleId) => _map.TryGetValue(ruleId, out var e) ? e : null;
    public bool Enabled(string ruleId) => !_map.TryGetValue(ruleId, out var e) || e.Enabled;

    /// <summary>Unknown rules run (safe default for new coded rules); known rules run only when Active + Enabled.</summary>
    public bool IsLive(string ruleId) => !_map.TryGetValue(ruleId, out var e) || e.IsLive;

    /// <summary>A copy with one rule's parameters replaced — used to dry-run a proposed threshold change.</summary>
    public RuleConfigStore WithOverride(string ruleId, string? configJson)
    {
        var copy = new RuleConfigStore();
        var entries = _map.Values.Select(e => e.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase)
            ? new RuleConfigEntry { RuleId = e.RuleId, Enabled = true, Status = RuleStatus.Active, ConfigJson = configJson, PlaybookJson = e.PlaybookJson }
            : e).ToList();
        if (!entries.Any(e => e.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase)))
            entries.Add(new RuleConfigEntry { RuleId = ruleId, ConfigJson = configJson });
        copy.Reload(entries);
        return copy;
    }

    public double Num(string ruleId, string key, double fallback)
    {
        var json = Get(ruleId)?.ConfigJson;
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var v)
                && v.TryGetDouble(out var d)) return d;
        }
        catch { /* malformed config → fall back */ }
        return fallback;
    }

    public int Int(string ruleId, string key, int fallback) => (int)Num(ruleId, key, fallback);

    public string[] Strings(string ruleId, string key)
    {
        var json = Get(ruleId)?.ConfigJson;
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var v)
                && v.ValueKind == JsonValueKind.Array)
                return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!).ToArray();
        }
        catch { /* ignore */ }
        return Array.Empty<string>();
    }
}
