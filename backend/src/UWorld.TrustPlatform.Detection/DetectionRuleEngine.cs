using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

/// <summary>
/// Runs every registered rule over one signal set and returns the union of cases,
/// de-duplicated by CaseKey so a single pass never yields the same case twice.
/// </summary>
public sealed class DetectionRuleEngine
{
    private readonly IReadOnlyList<IDetectionRule> _rules;
    private readonly RuleConfigStore? _config;
    private readonly IDeclarativeRuleSource? _declarative;

    public DetectionRuleEngine(IEnumerable<IDetectionRule> rules,
        RuleConfigStore? config = null, IDeclarativeRuleSource? declarative = null)
    {
        _rules = rules.ToList();
        _config = config;
        _declarative = declarative;
    }

    public IReadOnlyList<IDetectionRule> Rules => _rules;

    public IReadOnlyList<RiskCase> Evaluate(SignalSet signals)
    {
        var byKey = new Dictionary<string, RiskCase>(StringComparer.Ordinal);

        foreach (var rule in _rules)
        {
            if (_config is not null && !_config.Enabled(rule.RuleId)) continue;   // disabled via portal
            foreach (var riskCase in rule.Evaluate(signals))
                byKey.TryAdd(riskCase.CaseKey, riskCase);
        }

        // NL-authored declarative rules (added at runtime, no redeploy)
        if (_declarative is not null)
            foreach (var rule in _declarative.ActiveRules())
                foreach (var riskCase in rule.Evaluate(signals))
                    byKey.TryAdd(riskCase.CaseKey, riskCase);

        return byKey.Values.ToList();
    }
}

/// <summary>Supplies declarative (NL-authored) rules to the engine at evaluation time.</summary>
public interface IDeclarativeRuleSource
{
    IEnumerable<IDetectionRule> ActiveRules();
}
