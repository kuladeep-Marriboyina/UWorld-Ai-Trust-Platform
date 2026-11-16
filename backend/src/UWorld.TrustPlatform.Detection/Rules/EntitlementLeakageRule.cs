using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Evaluates entitlement at EVENT-TIME, not against current state. Access to a package the
/// account was once entitled to, but whose entitlement had expired / was cancelled / refunded
/// at the moment of access, is leakage.
/// </summary>
public sealed class EntitlementLeakageRule : IDetectionRule
{
    public string RuleId => "entitlement-leakage";
    public string Version => "v1";
    public string Category => "Entitlement";

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var entsByAccountPackage = s.Entitlements
            .GroupBy(e => (e.AccountId, e.PackageId))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var access in s.AccessEvents)
        {
            if (access.PackageId is not int package) continue;

            // Only consider accounts that WERE entitled to this package at some point;
            // "never purchased" scraping/ring traffic is caught by other rules.
            if (!entsByAccountPackage.TryGetValue((access.AccountId, package), out var ents)) continue;

            if (ents.Any(e => e.IsValidAt(access.OccurredUtc))) continue; // legitimately entitled at event-time

            var offending = ents.OrderByDescending(e => e.EndUtc).First();
            var reason = offending.RefundedUtc is not null && access.OccurredUtc >= offending.RefundedUtc
                ? $"refunded on {offending.RefundedUtc:u}"
                : $"expired on {offending.EndUtc:u}";

            yield return new RiskCase
            {
                CaseKey = $"{RuleId}:access:{access.AccessId}",
                RuleId = RuleId,
                RuleVersion = Version,
                Title = $"Package {package} used after the subscription was {(offending.RefundedUtc is not null ? "refunded" : "expired")}",
                Severity = RiskSeverity.High,
                Confidence = 0.9m,
                Score = 82m,
                BusinessImpactUsd = PackageListPrice(s, package),
                ScoreBreakdown =
                {
                    new ScoreFactor("SubscriptionStatus", 50m, $"Subscription {offending.EntitlementId} was {reason}."),
                    new ScoreFactor("AccessTime", 32m, "The user opened the content after the subscription had already ended."),
                },
                AccountId = access.AccountId,
                EntityRef = $"package:{package}",
                Expected = $"The user must have an active subscription to package {package} when they open it ({access.OccurredUtc:u}).",
                Actual = $"The subscription was {reason}, but the user still opened package {package} on {access.OccurredUtc:u}.",
                Evidence =
                {
                    Ev("AccessEvent", access.AccessId.ToString(), $"Access to package {package}", $"resource={access.Resource}", access.OccurredUtc),
                    Ev("Subscription", offending.EntitlementId.ToString(), $"Subscription {offending.Status}", $"valid {offending.StartUtc:u} to {offending.EndUtc:u}{(offending.RefundedUtc is null ? "" : $"; refunded {offending.RefundedUtc:u}")}", offending.EndUtc),
                }
            };
        }
    }

    private static decimal PackageListPrice(SignalSet s, int packageId)
    {
        var prices = s.Orders.Where(o => o.PackageId == packageId).Select(o => o.ListPrice).ToList();
        return prices.Count > 0 ? Math.Round(prices.Average(), 2) : 149m;
    }
}
