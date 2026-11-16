using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Discount / referral code abuse. Mirrors the real applydiscountcode validations (DiscountService):
/// over-limit, per-user reuse, expired-but-applied, Regular+Regular stacking, race double-use, and
/// PACKAGE ELIGIBILITY — a code applied to a package outside its discount group's GA_DISCOUNTS list.
/// Every case also names the associated subscription (status + last access) and the applydiscountcode
/// URL the user hit (from USER_LOGS).
/// </summary>
public sealed class DiscountCodeFraudRule : IDetectionRule
{
    public string RuleId => "discount-code-fraud";
    public string Version => "v3";
    public string Category => "Discount";

    private const decimal AvgBase = 199m;
    private const byte Regular = 1;
    private readonly RuleConfigStore _config;
    public DiscountCodeFraudRule(RuleConfigStore config) => _config = config;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var raceWindow = TimeSpan.FromSeconds(_config.Num(RuleId, "raceWindowSeconds", 60));
        var multiUse = _config.Strings(RuleId, "multipleUseCodes");
        var codes = s.ReferralCodes.ToDictionary(c => c.Code, c => c, StringComparer.OrdinalIgnoreCase);
        var payById = s.Payments2.ToDictionary(p => p.PaymentId, p => p);
        var eligibleByGroup = s.GaDiscounts.GroupBy(d => d.DiscountGroupId).ToDictionary(g => g.Key, g => g.Select(d => d.PackageId).ToHashSet());

        string PayCtx(int? pid)
        {
            if (pid is int id && payById.TryGetValue(id, out var p)) return $"payment {id}, user {p.UserId}, at {p.DateCreated:u}";
            return pid is int x ? $"payment {x}" : "no linked payment";
        }

        RiskCase Case(string key, string tag, string title, decimal impact, int? accountId, int? packageId, DateTime? when,
            string entityRef, string expected, string actual, params CaseEvidence[] ev)
        {
            var subCtx = SubscriptionContext(s, accountId, packageId);
            var url = ApplyDiscountUrl(s, accountId, when ?? s.EvaluatedUtc);
            var fullActual = actual + "  · " + subCtx + (url != null ? "  · applied from " + url : "");
            var evList = ev.ToList();
            var sub = ResolveSubscription(s, accountId, packageId);
            if (sub != null) evList.Add(Ev("Subscription", sub.SubscriptionId.ToString(), subCtx, null, LastAccessed(s, sub.SubscriptionId)));
            if (url != null) evList.Add(Ev("Url", "applydiscountcode", url, "from USER_LOGS", when));
            return new RiskCase
            {
                CaseKey = $"{RuleId}:{key}", RuleId = RuleId, RuleVersion = Version,
                Title = title, Severity = RiskSeverity.High, Confidence = 0.86m, Score = 76m,
                BusinessImpactUsd = Math.Round(impact, 2),
                ScoreBreakdown = { new ScoreFactor("DiscountPolicy", 76m, $"[{tag}] {actual}") },
                AccountId = accountId, EntityRef = entityRef, Expected = expected, Actual = fullActual, Evidence = evList
            };
        }

        // 1) over-limit
        foreach (var c in s.ReferralCodes.Where(c => c.Used > c.Limit))
        {
            var users = s.ReferralUsages.Where(u => u.Code.Equals(c.Code, StringComparison.OrdinalIgnoreCase)).Select(u => u.UserId).Distinct().OrderBy(x => x).ToList();
            yield return Case($"over-limit:{c.Code}", "OverLimit",
                $"Discount '{c.Code}' used {c.Used} times over a limit of {c.Limit}",
                (c.Used - c.Limit) * (c.Percent / 100m) * AvgBase, users.FirstOrDefault(), null, null, $"code:{c.Code}",
                $"Code '{c.Code}' may be used at most {c.Limit} times.",
                $"Code '{c.Code}' has {c.Used} uses ({c.Used - c.Limit} over limit); users seen: {(users.Count > 0 ? string.Join(", ", users) : "n/a")}.",
                Ev("ReferralCode", c.ReferralId.ToString(), $"'{c.Code}' used {c.Used}/{c.Limit}", $"{c.Percent}% off · group {c.DiscountGroupId}"));
        }

        // 2) expired-but-applied
        foreach (var u in s.ReferralUsages)
            if (codes.TryGetValue(u.Code, out var c) && u.DateUsed > c.ExpirationUtc)
                yield return Case($"expired:{u.Code}:{u.Id}", "Expired",
                    $"Expired discount '{u.Code}' applied by user {u.UserId} after {c.ExpirationUtc:u}",
                    c.Percent / 100m * AvgBase, u.UserId, null, u.DateUsed, $"code:{u.Code}",
                    $"Code '{u.Code}' expired on {c.ExpirationUtc:u}.",
                    $"User {u.UserId} applied '{u.Code}' on {u.DateUsed:u} ({PayCtx(u.PaymentId)}), after expiry {c.ExpirationUtc:u}.",
                    Ev("ReferralUsage", u.Id.ToString(), $"'{u.Code}' used by user {u.UserId}", PayCtx(u.PaymentId), u.DateUsed),
                    Ev("ReferralCode", c.ReferralId.ToString(), $"'{c.Code}' expired {c.ExpirationUtc:u}", null, c.ExpirationUtc));

        // 3) per-user reuse of a single-use code
        foreach (var g in s.ReferralUsages.GroupBy(u => (u.Code, u.UserId)))
        {
            if (g.Count() < 2) continue;
            if (multiUse.Contains(g.Key.Code, StringComparer.OrdinalIgnoreCase)) continue;
            if (codes.TryGetValue(g.Key.Code, out var c0) && c0.BehaviourTypeId != Regular) continue;
            var pct = codes.TryGetValue(g.Key.Code, out var cc) ? cc.Percent : 10m;
            var when = string.Join("; ", g.OrderBy(u => u.DateUsed).Select(u => $"{PayCtx(u.PaymentId)} on {u.DateUsed:u}"));
            yield return Case($"reuse:{g.Key.Code}:{g.Key.UserId}", "PerUserReuse",
                $"User {g.Key.UserId} reused single-use code '{g.Key.Code}' {g.Count()} times",
                (g.Count() - 1) * pct / 100m * AvgBase, g.Key.UserId, null, g.Min(u => u.DateUsed), $"code:{g.Key.Code}",
                "A single-use code may be used once per user.",
                $"User {g.Key.UserId} used '{g.Key.Code}' {g.Count()} times — {when}.",
                g.Select(u => Ev("ReferralUsage", u.Id.ToString(), $"'{u.Code}' by user {u.UserId}", PayCtx(u.PaymentId), u.DateUsed)).ToArray());
        }

        // 4) race double-use within seconds
        foreach (var g in s.ReferralUsages.GroupBy(u => u.Code))
        {
            var ordered = g.OrderBy(u => u.DateUsed).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].DateUsed - ordered[i - 1].DateUsed <= raceWindow && ordered[i].UserId != ordered[i - 1].UserId)
                {
                    var a = ordered[i - 1]; var b = ordered[i];
                    yield return Case($"race:{g.Key}", "Race",
                        $"Discount '{g.Key}' used by users {a.UserId} & {b.UserId} within {raceWindow.TotalSeconds:N0}s",
                        (codes.TryGetValue(g.Key, out var c) ? c.Percent : 10m) / 100m * AvgBase, a.UserId, null, a.DateUsed, $"code:{g.Key}",
                        "Concurrent uses must not bypass the usage counter.",
                        $"'{g.Key}' used by user {a.UserId} ({PayCtx(a.PaymentId)}) at {a.DateUsed:u} and user {b.UserId} ({PayCtx(b.PaymentId)}) at {b.DateUsed:u} — {(b.DateUsed - a.DateUsed).TotalSeconds:N0}s apart.",
                        Ev("ReferralUsage", a.Id.ToString(), $"'{g.Key}' user {a.UserId}", PayCtx(a.PaymentId), a.DateUsed),
                        Ev("ReferralUsage", b.Id.ToString(), $"'{g.Key}' user {b.UserId}", PayCtx(b.PaymentId), b.DateUsed));
                    break;
                }
        }

        // 5) stacking violation — two Regular codes on one order
        foreach (var g in s.PaymentDetails.Where(d => d.DiscountCode != null).GroupBy(d => d.PaymentId))
        {
            var applied = g.Select(d => d.DiscountCode!).Distinct(StringComparer.OrdinalIgnoreCase).Where(code => codes.ContainsKey(code)).ToList();
            var regulars = applied.Where(code => codes[code].BehaviourTypeId == Regular).ToList();
            if (regulars.Count < 2) continue;
            var pay = payById.TryGetValue(g.Key, out var pp) ? pp : null;
            yield return Case($"stacking:{g.Key}", "Stacking",
                $"Order {g.Key} stacks {regulars.Count} non-stackable codes ({string.Join(" + ", regulars)})",
                g.Sum(d => d.DiscountAmount), pay?.UserId, g.First().PackageId, pay?.DateCreated, $"payment:{g.Key}",
                "Regular (non-stackable) discount codes cannot be combined on one order.",
                $"{PayCtx(g.Key)} applied Regular codes {string.Join(" + ", regulars)} together.",
                g.Where(d => d.DiscountCode != null).Select(d => Ev("PaymentDetail", d.Id.ToString(), $"code '{d.DiscountCode}' on package {d.PackageId} (-${d.DiscountAmount})", PayCtx(g.Key), null)).ToArray());
        }

        // 6) PACKAGE ELIGIBILITY — code applied to a package outside its group's GA_DISCOUNTS list
        foreach (var d in s.PaymentDetails.Where(x => x.DiscountCode != null))
        {
            if (!codes.TryGetValue(d.DiscountCode!, out var code)) continue;
            if (code.DiscountGroupId is null || !eligibleByGroup.TryGetValue(code.DiscountGroupId.Value, out var eligible)) continue;
            if (eligible.Contains(d.PackageId)) continue;
            var pay = payById.TryGetValue(d.PaymentId, out var p2) ? p2 : null;
            yield return Case($"package-mismatch:{d.PaymentId}:{d.DiscountCode}:{d.PackageId}", "PackageMismatch",
                $"Discount '{d.DiscountCode}' applied to ineligible package {d.PackageId}",
                d.DiscountAmount, pay?.UserId, d.PackageId, pay?.DateCreated, $"payment:{d.PaymentId}",
                $"Code '{d.DiscountCode}' (group {code.DiscountGroupId}) is only valid for packages: {string.Join(", ", eligible.OrderBy(x => x))}.",
                $"Code '{d.DiscountCode}' was applied to package {d.PackageId} on {PayCtx(d.PaymentId)} — not in its eligible set [{string.Join(", ", eligible.OrderBy(x => x))}]. Gave away ${d.DiscountAmount}.",
                Ev("PaymentDetail", d.Id.ToString(), $"'{d.DiscountCode}' on package {d.PackageId} (-${d.DiscountAmount})", PayCtx(d.PaymentId), null),
                Ev("ReferralCode", code.ReferralId.ToString(), $"'{code.Code}' group {code.DiscountGroupId} eligible: {string.Join(",", eligible.OrderBy(x => x))}", null, null));
        }
    }
}
