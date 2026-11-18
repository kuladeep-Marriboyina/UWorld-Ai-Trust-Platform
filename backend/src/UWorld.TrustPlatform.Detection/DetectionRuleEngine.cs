using System.Text.Json;
using System.Text.RegularExpressions;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

/// <summary>
/// Runs every live rule over one signal set and returns the union of cases, de-duplicated by CaseKey.
/// Every case leaves the engine in the same shape — category, attack window, linked entities and a
/// playbook — whether a coded rule or an analyst-authored rule produced it.
/// </summary>
public sealed partial class DetectionRuleEngine
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
            if (_config is not null && !_config.IsLive(rule.RuleId)) continue;   // disabled / pending in the portal
            foreach (var riskCase in rule.Evaluate(signals))
                byKey.TryAdd(riskCase.CaseKey, Enrich(riskCase, rule, _config));
        }

        if (_declarative is not null)
            foreach (var rule in _declarative.ActiveRules())
                foreach (var riskCase in rule.Evaluate(signals))
                    byKey.TryAdd(riskCase.CaseKey, Enrich(riskCase, rule, _config));

        return byKey.Values.ToList();
    }

    /// <summary>Evaluate a single rule in isolation (dry-runs / backtests); nothing is persisted.</summary>
    public static IReadOnlyList<RiskCase> EvaluateOne(IDetectionRule rule, SignalSet signals, RuleConfigStore? config = null)
        => rule.Evaluate(signals).GroupBy(c => c.CaseKey).Select(g => Enrich(g.First(), rule, config)).ToList();

    [GeneratedRegex(@"\bip=(\d{1,3}(?:\.\d{1,3}){3})")] private static partial Regex IpInDetail();
    [GeneratedRegex(@"\bdevice=([A-Za-z0-9\-]+)")] private static partial Regex DeviceInDetail();

    private static RiskCase Enrich(RiskCase c, IDetectionRule rule, RuleConfigStore? config)
    {
        c.Category ??= rule.Category;
        var times = c.Evidence.Where(e => e.OccurredUtc != null).Select(e => e.OccurredUtc!.Value).ToList();
        if (times.Count > 0)
        {
            c.FirstSignalUtc ??= times.Min();
            c.TriggerSignalUtc ??= times.Max();
        }

        var set = c.Entities.Select(e => (e.Type, e.Value)).ToHashSet();
        void Add(string t, string v) { if (!string.IsNullOrWhiteSpace(v) && set.Add((t, v))) c.Entities.Add(new CaseEntity(t, v)); }
        if (c.AccountId is int uid) Add("user", uid.ToString());
        foreach (var e in c.Evidence)
        {
            var text = $"{e.Label} {e.Detail}";
            foreach (Match m in IpInDetail().Matches(text)) Add("ip", m.Groups[1].Value);
            foreach (Match m in DeviceInDetail().Matches(text)) Add("device", m.Groups[1].Value);
            if (e.SourceType is "Subscription") Add("subscription", e.SourceId);
            if (e.SourceType is "ReferralCode" or "ReferralUsage" && Regex.Match(e.Label, "'([^']+)'") is { Success: true } code) Add("code", code.Groups[1].Value);
        }
        if (c.EntityRef is { } er && er.Split(':', 2) is [var t, var v] && t is "subscription" or "code" or "payment" or "ip")
            Add(t, v);

        // rule-level playbook configured in the portal applies to every case the rule raises
        var rulePlaybook = config?.Get(rule.RuleId)?.PlaybookJson;
        if (!string.IsNullOrWhiteSpace(rulePlaybook))
        {
            try
            {
                foreach (var step in JsonSerializer.Deserialize<List<string>>(rulePlaybook) ?? new())
                    if (!c.Playbook.Contains(step)) c.Playbook.Add(step);
            }
            catch { /* malformed playbook → ignore */ }
        }
        if (c.Playbook.Count == 0) c.Playbook.AddRange(DefaultPlaybook(c.RuleId));
        return c;
    }

    private static IEnumerable<string> DefaultPlaybook(string ruleId) => ruleId switch
    {
        "paid-without-braintree" => new[] { "Suspend access granted by this order until a gateway capture is confirmed.", "Reconcile the order against Braintree settlement reports.", "Trace which checkout code path wrote PaymentStatus=Paid without a transaction id." },
        "discount-code-fraud" => new[] { "Deactivate or cap the abused code.", "Re-price the affected orders or revoke the discount.", "Check coupon-sharing sites for the code." },
        "same-subscription-region" => new[] { "Force re-authentication on the subscription and cap concurrent sessions.", "Contact the subscriber about credential sharing per the terms of use." },
        "blocked-ip-embargo" => new[] { "Confirm the edge/WAF block list is enforced for this source.", "Terminate any active session from the source." },
        "authorization-gap" => new[] { "Hot-fix the endpoint to short-circuit on Denied/Missing authorization.", "Quantify content served on this correlation id from egress logs." },
        "entitlement-leakage" => new[] { "Invalidate cached entitlements for the account.", "Revoke access to content opened after the subscription ended." },
        _ => new[] { "Review the evidence and corroborate with a second independent source before acting." }
    };
}

/// <summary>Supplies authored rules to the engine at evaluation time.</summary>
public interface IDeclarativeRuleSource
{
    IEnumerable<IDetectionRule> ActiveRules();
}
