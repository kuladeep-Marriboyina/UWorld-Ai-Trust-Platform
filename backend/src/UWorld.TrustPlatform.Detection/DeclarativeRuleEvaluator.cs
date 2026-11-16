using System.Globalization;
using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection;

/// <summary>A signal row exposed to declarative rules by its real field names.</summary>
public sealed record SignalRow(string Id, int? UserId, IReadOnlyDictionary<string, object?> Fields);

public static class SignalRowAdapter
{
    public static IEnumerable<SignalRow> Rows(SignalSet s, string target) => target.ToUpperInvariant() switch
    {
        "USER_PAYMENTS" => s.Payments2.Select(p => new SignalRow(p.PaymentId.ToString(), p.UserId, new Dictionary<string, object?>
        {
            ["PaymentStatus"] = p.PaymentStatus, ["FinalAmount"] = p.FinalAmount, ["TransactionAmount"] = p.TransactionAmount,
            ["GatewayId"] = p.GatewayId, ["TransactionId"] = p.TransactionId, ["PaymentModeId"] = p.PaymentModeId,
            ["CountryCode"] = p.CountryCode, ["UserId"] = p.UserId
        })),
        "USER_LOGS" => s.UserLogs.Select(l => new SignalRow(l.Id.ToString(), l.UserId, new Dictionary<string, object?>
        {
            ["CountryCode"] = l.CountryCode, ["IPAddress"] = l.IPAddress, ["SubscriptionId"] = l.SubscriptionId, ["DeviceTypeId"] = l.DeviceTypeId
        })),
        "NCLEX_REFERRAL_CODES" => s.ReferralCodes.Select(c => new SignalRow(c.ReferralId.ToString(), null, new Dictionary<string, object?>
        {
            ["ReferralCodeUsed"] = c.Used, ["ReferralCodeLimit"] = c.Limit, ["DiscountPercent"] = c.Percent,
            ["DiscountBehaviourTypeId"] = c.BehaviourTypeId, ["ReferralCode"] = c.Code
        })),
        "USER_SUBSCRIPTIONS" => s.Subscriptions.Select(sub => new SignalRow(sub.SubscriptionId.ToString(), sub.UserId, new Dictionary<string, object?>
        {
            ["Active"] = sub.Active, ["PackageId"] = sub.PackageId, ["CourseId"] = sub.CourseId, ["UserId"] = sub.UserId
        })),
        _ => Enumerable.Empty<SignalRow>()
    };
}

/// <summary>Runs one NL-authored <see cref="DeclarativeRule"/> as a detection rule.</summary>
public sealed class DeclarativeRuleEvaluator : IDetectionRule
{
    private readonly DeclarativeRule _rule;
    public DeclarativeRuleEvaluator(DeclarativeRule rule) => _rule = rule;

    public string RuleId => _rule.RuleId;
    public string Version => "nl-v1";
    public string Category => _rule.Category;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var sev = Enum.TryParse<RiskSeverity>(_rule.Severity, true, out var r) ? r : RiskSeverity.Medium;

        foreach (var row in SignalRowAdapter.Rows(s, _rule.Target))
        {
            if (!_rule.When.All(c => Match(row, c))) continue;

            decimal impact = 0m;
            if (_rule.ImpactField is not null && row.Fields.TryGetValue(_rule.ImpactField, out var iv) && TryDecimal(iv, out var d)) impact = d;

            yield return new RiskCase
            {
                CaseKey = $"{_rule.RuleId}:{_rule.Target}:{row.Id}",
                RuleId = _rule.RuleId, RuleVersion = Version,
                Title = $"{_rule.Name} — {_rule.Target} {row.Id}",
                Severity = sev, Confidence = 0.8m, Score = sev == RiskSeverity.Critical ? 90 : sev == RiskSeverity.High ? 78 : 60,
                BusinessImpactUsd = impact,
                ScoreBreakdown = { new ScoreFactor("CustomRule", 100m, $"Matched authored rule: {_rule.EnglishSource}") },
                AccountId = row.UserId, EntityRef = $"{_rule.Target}:{row.Id}",
                Expected = "Behaviour permitted by policy.",
                Actual = $"Row {row.Id} matched: {string.Join(" AND ", _rule.When.Select(c => $"{c.Field} {c.Op} {c.Value ?? string.Join('/', c.Values ?? Array.Empty<string>())}"))}.",
                Evidence = { Ev(_rule.Target, row.Id, _rule.Name, _rule.EnglishSource, null) }
            };
        }
    }

    private static bool Match(SignalRow row, RuleCondition c)
    {
        row.Fields.TryGetValue(c.Field, out var actual);
        switch (c.Op.ToLowerInvariant())
        {
            case "in": return c.Values?.Any(v => Eq(actual, v)) ?? false;
            case "notin": return !(c.Values?.Any(v => Eq(actual, v)) ?? false);
            case "contains": return (actual?.ToString() ?? "").Contains(c.Value ?? "", StringComparison.OrdinalIgnoreCase);
            case "ne": return !Eq(actual, c.Value);
            case "eq": return Eq(actual, c.Value);
        }
        if (TryDecimal(actual, out var a) && decimal.TryParse(c.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
            return c.Op.ToLowerInvariant() switch { "gt" => a > b, "lt" => a < b, "gte" => a >= b, "lte" => a <= b, _ => false };
        return false;
    }

    private static bool Eq(object? actual, string? value) =>
        string.Equals(actual?.ToString(), value, StringComparison.OrdinalIgnoreCase);

    private static bool TryDecimal(object? v, out decimal d)
    {
        switch (v)
        {
            case null: d = 0; return false;
            case decimal m: d = m; return true;
            case int i: d = i; return true;
            case byte b: d = b; return true;
            case short s: d = s; return true;
            default: return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
        }
    }
}
