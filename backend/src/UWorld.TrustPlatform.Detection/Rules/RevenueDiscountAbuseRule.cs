using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Three revenue-erosion patterns: unexpected zero-pay purchases, price-below-policy orders
/// (with no discount that would explain it), and a single discount code reused across accounts
/// that belong to the same linked group.
/// </summary>
public sealed class RevenueDiscountAbuseRule : IDetectionRule
{
    public string RuleId => "revenue-abuse";
    public string Version => "v1";
    public string Category => "Revenue";

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var discountedOrderIds = s.Discounts.Select(d => d.OrderId).ToHashSet();

        foreach (var o in s.Orders)
        {
            var isTrial = string.Equals(o.OrderType, "Trial", StringComparison.OrdinalIgnoreCase);

            // (a) unexpected zero-pay on a real purchase
            if (!isTrial && o.PaidAmount <= 0m)
            {
                yield return new RiskCase
                {
                    CaseKey = $"{RuleId}:zero-pay:{o.OrderId}",
                    RuleId = RuleId, RuleVersion = Version,
                    Title = $"Unexpected zero-pay {o.OrderType} order {o.OrderId}",
                    Severity = RiskSeverity.High, Confidence = 0.88m, Score = 80m,
                    BusinessImpactUsd = Math.Round(o.ListPrice - o.PaidAmount, 2),
                    ScoreBreakdown = { new ScoreFactor("PaidAmount", 80m, $"{o.OrderType} order fulfilled for $0.00 (list ${o.ListPrice}).") },
                    AccountId = o.AccountId, EntityRef = $"order:{o.OrderId}",
                    Expected = $"A {o.OrderType} order should capture at least the policy minimum of ${o.PolicyMinPrice}.",
                    Actual = $"Order {o.OrderId} captured $0.00 against a list price of ${o.ListPrice}.",
                    Evidence = { Ev("Order", o.OrderId.ToString(), $"{o.OrderType} order for $0.00", $"list=${o.ListPrice}; policyMin=${o.PolicyMinPrice}", o.OrderUtc) }
                };
            }
            // (b) price below policy, with no discount explaining the gap
            else if (!isTrial && o.PaidAmount > 0m && o.PaidAmount < o.PolicyMinPrice && !discountedOrderIds.Contains(o.OrderId))
            {
                yield return new RiskCase
                {
                    CaseKey = $"{RuleId}:below-policy:{o.OrderId}",
                    RuleId = RuleId, RuleVersion = Version,
                    Title = $"Order {o.OrderId} priced below policy minimum",
                    Severity = RiskSeverity.Medium, Confidence = 0.8m, Score = 64m,
                    BusinessImpactUsd = Math.Round(o.PolicyMinPrice - o.PaidAmount, 2),
                    ScoreBreakdown = { new ScoreFactor("PaidAmount", 64m, $"Paid ${o.PaidAmount} vs policy minimum ${o.PolicyMinPrice} with no discount on record.") },
                    AccountId = o.AccountId, EntityRef = $"order:{o.OrderId}",
                    Expected = $"Paid amount must be >= policy minimum ${o.PolicyMinPrice} unless a discount applies.",
                    Actual = $"Order {o.OrderId} captured ${o.PaidAmount} with no discount recorded.",
                    Evidence = { Ev("Order", o.OrderId.ToString(), $"Paid ${o.PaidAmount} below policy min ${o.PolicyMinPrice}", null, o.OrderUtc) }
                };
            }
        }

        // (c) one discount code reused across accounts in the same linked group
        var groupByAccount = s.Accounts
            .Where(a => a.LinkedAccountGroupId is not null)
            .ToDictionary(a => a.AccountId, a => a.LinkedAccountGroupId!.Value);

        foreach (var byCode in s.Discounts.GroupBy(d => d.Code))
        {
            var linked = byCode
                .Where(d => groupByAccount.ContainsKey(d.AccountId))
                .GroupBy(d => groupByAccount[d.AccountId])
                .Where(g => g.Select(d => d.AccountId).Distinct().Count() > 1)
                .ToList();

            foreach (var group in linked)
            {
                var accounts = group.Select(d => d.AccountId).Distinct().OrderBy(x => x).ToList();
                var reuseImpact = group.Sum(d =>
                {
                    var list = s.Orders.FirstOrDefault(o => o.OrderId == d.OrderId)?.ListPrice ?? 0m;
                    return d.Percent / 100m * list;
                });
                yield return new RiskCase
                {
                    CaseKey = $"{RuleId}:discount-reuse:{byCode.Key}:{group.Key}",
                    RuleId = RuleId, RuleVersion = Version,
                    Title = $"Discount '{byCode.Key}' reused across {accounts.Count} linked accounts",
                    Severity = RiskSeverity.High, Confidence = 0.85m, Score = 78m,
                    BusinessImpactUsd = Math.Round(reuseImpact, 2),
                    ScoreBreakdown =
                    {
                        new ScoreFactor("DiscountReuse", 48m, $"Code '{byCode.Key}' applied by {accounts.Count} accounts."),
                        new ScoreFactor("LinkedGroup", 30m, $"Accounts {string.Join(", ", accounts)} share linked group {group.Key}."),
                    },
                    EntityRef = $"discount:{byCode.Key}",
                    Expected = "A single-use discount code should not be shared across linked accounts.",
                    Actual = $"Code '{byCode.Key}' used by accounts {string.Join(", ", accounts)} (linked group {group.Key}).",
                    Evidence = group.Select(d => Ev("Discount", d.DiscountId.ToString(), $"'{d.Code}' on order {d.OrderId} by account {d.AccountId}", $"{d.Percent}% off", d.AppliedUtc)).ToList()
                };
            }
        }
    }
}
