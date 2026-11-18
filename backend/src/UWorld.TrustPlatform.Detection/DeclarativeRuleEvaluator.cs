using System.Globalization;
using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection;

/// <summary>A signal row exposed to authored rules by its real column names, plus the time it happened.</summary>
public sealed record SignalRow(string Id, int? UserId, DateTime? Time, IReadOnlyDictionary<string, object?> Fields);

public static class SignalRowAdapter
{
    public static IEnumerable<SignalRow> Rows(SignalSet s, string target) => target.ToUpperInvariant() switch
    {
        "USERS" => s.Users.Select(u => new SignalRow(u.UserId.ToString(), u.UserId, u.DateCreated, new Dictionary<string, object?>
        {
            ["UserId"] = u.UserId, ["Email"] = u.Email, ["EmailDomain"] = IdentityAnalysis.Domain(u.Email),
            ["EmailPattern"] = IdentityAnalysis.Stem(u.Email), ["Mailbox"] = IdentityAnalysis.Mailbox(u.Email),
            ["IsDisposableEmail"] = IdentityAnalysis.IsDisposable(u.Email),
            ["FirstName"] = u.FirstName, ["LastName"] = u.LastName, ["CountryCode"] = u.CountryCode,
            ["RegistrationIp"] = u.RegistrationIp, ["DeviceFingerprint"] = u.DeviceFingerprint,
            ["UserAgent"] = u.UserAgent, ["IsAutomationClient"] = IdentityAnalysis.IsAutomationClient(u.UserAgent),
            ["SignupSource"] = u.SignupSource
        })),
        "API_LOGS" => s.ApiLogs.Select(l => new SignalRow(l.Id.ToString(), l.UserId, l.DateCreated, new Dictionary<string, object?>
        {
            ["Endpoint"] = l.Endpoint, ["Method"] = l.Method, ["StatusCode"] = l.StatusCode, ["Outcome"] = l.Outcome,
            ["IPAddress"] = l.IPAddress, ["CountryCode"] = l.CountryCode, ["Email"] = l.Email, ["UserId"] = l.UserId,
            ["UserAgent"] = l.UserAgent, ["DeviceFingerprint"] = l.DeviceFingerprint,
            ["IsAutomationClient"] = IdentityAnalysis.IsAutomationClient(l.UserAgent)
        })),
        "USER_PAYMENTS" => s.Payments2.Select(p => new SignalRow(p.PaymentId.ToString(), p.UserId, p.DateCreated, new Dictionary<string, object?>
        {
            ["PaymentStatus"] = p.PaymentStatus, ["FinalAmount"] = p.FinalAmount, ["TransactionAmount"] = p.TransactionAmount,
            ["DiscountAmount"] = p.DiscountAmount, ["GatewayId"] = p.GatewayId, ["TransactionId"] = p.TransactionId,
            ["PaymentModeId"] = p.PaymentModeId, ["PaymentTypeId"] = p.PaymentTypeId, ["SourceId"] = p.SourceId,
            ["CountryCode"] = p.CountryCode, ["IPAddress"] = p.IPAddress, ["UserId"] = p.UserId,
            ["CardFingerprint"] = p.CardFingerprint, ["CardLast4"] = p.CardLast4
        })),
        "USER_LOGS" => s.UserLogs.Select(l => new SignalRow(l.Id.ToString(), l.UserId, l.DateCreated, new Dictionary<string, object?>
        {
            ["UserId"] = l.UserId, ["CountryCode"] = l.CountryCode, ["IPAddress"] = l.IPAddress, ["SubscriptionId"] = l.SubscriptionId,
            ["DeviceTypeId"] = l.DeviceTypeId, ["LogType"] = l.LogType, ["Url"] = l.Url
        })),
        "USER_SUBSCRIPTIONS" => s.Subscriptions.Select(sub => new SignalRow(sub.SubscriptionId.ToString(), sub.UserId, sub.DateActivated, new Dictionary<string, object?>
        {
            ["Active"] = sub.Active, ["PackageId"] = sub.PackageId, ["CourseId"] = sub.CourseId, ["UserId"] = sub.UserId,
            ["ListPrice"] = s.PriceOf(sub.PackageId)
        })),
        "NCLEX_REFERRAL_CODES" => s.ReferralCodes.Select(c => new SignalRow(c.ReferralId.ToString(), null, null, new Dictionary<string, object?>
        {
            ["ReferralCodeUsed"] = c.Used, ["ReferralCodeLimit"] = c.Limit, ["DiscountPercent"] = c.Percent,
            ["DiscountBehaviourTypeId"] = c.BehaviourTypeId, ["ReferralCode"] = c.Code
        })),
        "REFERRAL_CODE_USAGE" => s.ReferralUsages.Select(u => new SignalRow(u.Id.ToString(), u.UserId, u.DateUsed, new Dictionary<string, object?>
        {
            ["ReferralCode"] = u.Code, ["UserId"] = u.UserId, ["PaymentId"] = u.PaymentId
        })),
        _ => Enumerable.Empty<SignalRow>()
    };

    /// <summary>Map a column to the entity type it identifies, so authored cases link like coded ones.</summary>
    public static string? EntityTypeOf(string field) => field.ToLowerInvariant() switch
    {
        "ipaddress" or "registrationip" => "ip",
        "devicefingerprint" => "device",
        "email" or "mailbox" => "email",
        "userid" => "user",
        "cardfingerprint" => "card",
        "emaildomain" => "domain",
        "referralcode" => "code",
        "subscriptionid" => "subscription",
        _ => null
    };
}

/// <summary>Runs one authored <see cref="DeclarativeRule"/> as a detection rule.</summary>
public sealed class DeclarativeRuleEvaluator : IDetectionRule
{
    private readonly DeclarativeRule _rule;
    public DeclarativeRuleEvaluator(DeclarativeRule rule) => _rule = rule;

    public string RuleId => _rule.RuleId;
    public string Version => "authored";
    public string Category => _rule.Category;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var rows = SignalRowAdapter.Rows(s, _rule.Target)
            .Where(r => _rule.When.All(c => Match(r, c)))
            .Where(r => !_rule.Exclusions.Any(c => Match(r, c)))
            .ToList();

        if (string.IsNullOrWhiteSpace(_rule.GroupBy))
        {
            foreach (var row in rows)
                yield return BuildCase($"{_rule.RuleId}:{_rule.Target}:{row.Id}", $"{_rule.Target} row {row.Id}", new[] { row }, null, null);
            yield break;
        }

        var threshold = _rule.ThresholdValue ?? 1m;
        foreach (var group in rows.GroupBy(r => r.Fields.TryGetValue(_rule.GroupBy!, out var v) ? v?.ToString() : null).Where(g => !string.IsNullOrEmpty(g.Key)))
        {
            var ordered = group.OrderBy(r => r.Time ?? DateTime.MinValue).ToList();
            if (_rule.WindowMinutes is not int wm || wm <= 0 || ordered.All(r => r.Time is null))
            {
                var agg = Aggregate(ordered);
                if (Meets(agg, threshold))
                    yield return BuildCase($"{_rule.RuleId}:{group.Key}", $"{_rule.GroupBy} = {group.Key}", ordered, group.Key, agg);
                continue;
            }

            var window = TimeSpan.FromMinutes(wm);
            var i = 0;
            while (i < ordered.Count)
            {
                var start = ordered[i].Time!.Value;
                var inWin = ordered.Skip(i).TakeWhile(r => r.Time <= start + window).ToList();
                var agg = Aggregate(inWin);
                if (!Meets(agg, threshold)) { i++; continue; }

                // extend the burst while rows keep arriving inside the window, so a growing attack stays one case
                var burst = new List<SignalRow>(inWin);
                var j = i + inWin.Count;
                while (j < ordered.Count && ordered[j].Time - burst[^1].Time <= window) { burst.Add(ordered[j]); j++; }
                yield return BuildCase($"{_rule.RuleId}:{group.Key}:{ordered[i].Id}", $"{_rule.GroupBy} = {group.Key}", burst, group.Key, Aggregate(burst));
                i = j;
            }
        }
    }

    private decimal Aggregate(IReadOnlyList<SignalRow> rows) => (_rule.Aggregate ?? "count").ToLowerInvariant() switch
    {
        "distinct" => rows.Select(r => r.Fields.TryGetValue(_rule.AggregateField ?? "", out var v) ? v?.ToString() : null)
                          .Where(v => v != null).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        "sum" => rows.Sum(r => r.Fields.TryGetValue(_rule.AggregateField ?? "", out var v) && TryDecimal(v, out var d) ? d : 0m),
        _ => rows.Count
    };

    private bool Meets(decimal value, decimal threshold) => (_rule.ThresholdOp ?? "gte").ToLowerInvariant() switch
    {
        "gt" => value > threshold, "eq" => value == threshold, "lte" => value <= threshold, "lt" => value < threshold, _ => value >= threshold
    };

    private string AggregateLabel() => (_rule.Aggregate ?? "count").ToLowerInvariant() switch
    {
        "distinct" => $"distinct {_rule.AggregateField}",
        "sum" => $"sum of {_rule.AggregateField}",
        _ => "matching rows"
    };

    private RiskCase BuildCase(string key, string subject, IReadOnlyList<SignalRow> rows, string? groupValue, decimal? agg)
    {
        var sev = Enum.TryParse<RiskSeverity>(_rule.Severity, true, out var r) ? r : RiskSeverity.Medium;
        var baseScore = sev switch { RiskSeverity.Critical => 90m, RiskSeverity.High => 76m, RiskSeverity.Medium => 60m, _ => 45m };
        var threshold = _rule.ThresholdValue ?? 1m;
        var excess = agg is decimal a && threshold > 0 ? Math.Min(9m, Math.Max(0m, (a / threshold - 1m) * 10m)) : 0m;

        decimal impact; string formula;
        if (_rule.ImpactField is { Length: > 0 } f)
        {
            impact = rows.Sum(x => x.Fields.TryGetValue(f, out var v) && TryDecimal(v, out var d) ? d : 0m);
            formula = $"Sum of {f} over {rows.Count} matched row(s) = ${impact:N2}.";
        }
        else if (_rule.ImpactPerMatchUsd is decimal per)
        {
            impact = per * rows.Count;
            formula = $"{rows.Count} matched row(s) × ${per:N2} per match (set by the rule author) = ${impact:N2}.";
        }
        else { impact = 0m; formula = "This rule reports risk without a direct $ figure (no impact field configured)."; }

        var conds = string.Join(" AND ", _rule.When.Select(c => c.ToString()));
        var times = rows.Where(x => x.Time != null).Select(x => x.Time!.Value).ToList();
        var rc = new RiskCase
        {
            CaseKey = key, RuleId = _rule.RuleId, RuleVersion = Version, Category = _rule.Category,
            Title = agg is decimal ag ? $"{_rule.Name} — {subject}: {ag:0.##} {AggregateLabel()}" : $"{_rule.Name} — {subject}",
            Severity = sev, Confidence = _rule.Confidence, Score = baseScore + excess,
            BusinessImpactUsd = Math.Round(impact, 2), ImpactFormula = formula,
            ScoreBreakdown =
            {
                new ScoreFactor("AuthoredRule", baseScore, $"Matched: {(conds.Length > 0 ? conds : "all rows")}{(_rule.Exclusions.Count > 0 ? $"; excluding {string.Join(" OR ", _rule.Exclusions.Select(c => c.ToString()))}" : "")}."),
            },
            AccountId = rows.Select(x => x.UserId).FirstOrDefault(u => u != null),
            EntityRef = groupValue != null ? $"{_rule.GroupBy}:{groupValue}" : $"{_rule.Target}:{rows[0].Id}",
            Expected = _rule.Description ?? "Behaviour permitted by policy.",
            Actual = agg is decimal ag2
                ? $"{subject}: {ag2:0.##} {AggregateLabel()} (threshold {_rule.ThresholdOp} {threshold:0.##}{(_rule.WindowMinutes is int w ? $" within {w} min" : "")})."
                : $"{_rule.Target} row {rows[0].Id} matched {conds}.",
            FirstSignalUtc = times.Count > 0 ? times.Min() : null,
            TriggerSignalUtc = times.Count > 0 ? times.Max() : null,
        };
        if (excess > 0) rc.ScoreBreakdown.Add(new ScoreFactor("ThresholdExceeded", excess, $"Observed {agg:0.##} vs threshold {threshold:0.##}."));

        foreach (var row in rows.Take(30))
            rc.Evidence.Add(Ev(_rule.Target, row.Id, Describe(row), _rule.EnglishSource.Length > 0 ? $"rule: {_rule.EnglishSource}" : null, row.Time));

        if (groupValue != null && SignalRowAdapter.EntityTypeOf(_rule.GroupBy!) is string gt) rc.Entities.Add(new CaseEntity(gt, groupValue));
        foreach (var u in rows.Select(x => x.UserId).Where(u => u != null).Distinct().Take(30)) rc.Entities.Add(new CaseEntity("user", u!.Value.ToString()));
        rc.Playbook.AddRange(_rule.Playbook);
        return rc;
    }

    private static string Describe(SignalRow row)
    {
        var keys = new[] { "Email", "RegistrationIp", "IPAddress", "Endpoint", "Outcome", "FinalAmount", "ReferralCode", "CountryCode", "UserId" };
        var parts = keys.Where(k => row.Fields.TryGetValue(k, out var v) && v != null).Take(4).Select(k => $"{k}={row.Fields[k]}");
        return string.Join(" · ", parts);
    }

    internal static bool Match(SignalRow row, RuleCondition c)
    {
        row.Fields.TryGetValue(c.Field, out var actual);
        switch (c.Op.ToLowerInvariant())
        {
            case "in": return c.Values?.Any(v => Eq(actual, v)) ?? false;
            case "notin": return !(c.Values?.Any(v => Eq(actual, v)) ?? false);
            case "contains": return (actual?.ToString() ?? "").Contains(c.Value ?? "", StringComparison.OrdinalIgnoreCase);
            case "startswith": return (actual?.ToString() ?? "").StartsWith(c.Value ?? "", StringComparison.OrdinalIgnoreCase);
            case "ne": return !Eq(actual, c.Value);
            case "eq": return Eq(actual, c.Value);
            case "isnull": return actual is null || actual.ToString() == "";
            case "notnull": return actual is not null && actual.ToString() != "";
        }
        if (TryDecimal(actual, out var a) && decimal.TryParse(c.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
            return c.Op.ToLowerInvariant() switch { "gt" => a > b, "lt" => a < b, "gte" => a >= b, "lte" => a <= b, _ => false };
        return false;
    }

    private static bool Eq(object? actual, string? value)
    {
        if (actual is bool bv) return string.Equals(value, bv ? "true" : "false", StringComparison.OrdinalIgnoreCase) || value == (bv ? "1" : "0");
        if (TryDecimal(actual, out var a) && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var b)) return a == b;
        return string.Equals(actual?.ToString() ?? "", value ?? "", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryDecimal(object? v, out decimal d)
    {
        switch (v)
        {
            case null: d = 0; return false;
            case bool: d = 0; return false;
            case decimal m: d = m; return true;
            case int i: d = i; return true;
            case long l: d = l; return true;
            case byte b: d = b; return true;
            case short s: d = s; return true;
            default: return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
        }
    }
}
