using System.Text.Json;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

/// <summary>
/// Supplies the engine with the NL-authored declarative rules currently enabled in the config store
/// (deserialized from RULE_DEFINITIONS.DeclarativeJson). Picked up on the next ingestion pass — no redeploy.
/// </summary>
public sealed class DeclarativeRuleSource : IDeclarativeRuleSource
{
    private readonly RuleConfigStore _config;
    public DeclarativeRuleSource(RuleConfigStore config) => _config = config;

    public IEnumerable<IDetectionRule> ActiveRules()
    {
        foreach (var entry in _config.All)
        {
            if (!entry.IsDeclarative || !entry.IsLive || string.IsNullOrWhiteSpace(entry.DeclarativeJson)) continue;
            DeclarativeRule? rule = null;
            try { rule = JsonSerializer.Deserialize<DeclarativeRule>(entry.DeclarativeJson); } catch { }
            if (rule is not null) yield return new DeclarativeRuleEvaluator(rule);
        }
    }
}
