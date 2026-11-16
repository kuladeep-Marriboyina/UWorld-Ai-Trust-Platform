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
