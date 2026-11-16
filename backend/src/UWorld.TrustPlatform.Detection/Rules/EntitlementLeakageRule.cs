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
                Title = $"Access to package {package} after entitlement {(offending.RefundedUtc is not null ? "refund" : "expiry")}",
                Severity = RiskSeverity.High,
                Confidence = 0.9m,
                Score = 82m,
                BusinessImpactUsd = PackageListPrice(s, package),
                ScoreBreakdown =
                {
                    new ScoreFactor("EntitlementState", 50m, $"Entitlement {offending.EntitlementId} was {reason}."),
                    new ScoreFactor("EventTimeCheck", 32m, "Access timestamp fell outside the valid entitlement window."),
                },
                AccountId = access.AccountId,
                EntityRef = $"package:{package}",
                Expected = $"Access to package {package} requires an entitlement valid at {access.OccurredUtc:u}.",
                Actual = $"Entitlement {offending.EntitlementId} was {reason}; access occurred at {access.OccurredUtc:u}.",
                Evidence =
                {
                    Ev("AccessEvent", access.AccessId.ToString(), $"Access to package {package}", $"resource={access.Resource}", access.OccurredUtc),
                    Ev("Entitlement", offending.EntitlementId.ToString(), $"Entitlement {offending.Status}", $"window {offending.StartUtc:u}..{offending.EndUtc:u}; refunded={offending.RefundedUtc:u}", offending.EndUtc),
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
