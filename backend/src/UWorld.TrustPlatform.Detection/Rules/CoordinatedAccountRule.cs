using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Scores multi-account abuse on several weighted signals and deliberately refuses to fire on
/// a shared IP alone (offices, campuses and VPNs share IPs). A case needs at least two distinct
/// corroborating signal types.
/// </summary>
public sealed class CoordinatedAccountRule : IDetectionRule
{
    public string RuleId => "coordinated-account";
    public string Version => "v1";
    public string Category => "Account";

    private const decimal Threshold = 40m;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var groups = s.Accounts.Where(a => a.LinkedAccountGroupId is not null).GroupBy(a => a.LinkedAccountGroupId!.Value);

        foreach (var group in groups)
        {
            var accountIds = group.Select(a => a.AccountId).ToHashSet();
            if (accountIds.Count < 2) continue;

            var access = s.AccessEvents.Where(a => accountIds.Contains(a.AccountId)).ToList();
            var factors = new List<ScoreFactor>();

            var sharedDevices = access.GroupBy(a => a.DeviceId)
                .Where(g => g.Select(a => a.AccountId).Distinct().Count() > 1).Select(g => g.Key).ToList();
            if (sharedDevices.Count > 0)
                factors.Add(new ScoreFactor("SharedDevice", 30m, $"Device(s) {string.Join(", ", sharedDevices)} used by multiple accounts."));

            var reusedDiscounts = s.Discounts.Where(d => accountIds.Contains(d.AccountId))
                .GroupBy(d => d.Code).Where(g => g.Select(d => d.AccountId).Distinct().Count() > 1).Select(g => g.Key).ToList();
            if (reusedDiscounts.Count > 0)
                factors.Add(new ScoreFactor("ReusedDiscount", 25m, $"Discount code(s) {string.Join(", ", reusedDiscounts)} shared across accounts."));

            var sharedIps = access.GroupBy(a => a.IpAddress)
                .Where(g => g.Select(a => a.AccountId).Distinct().Count() > 1).Select(g => g.Key).ToList();
            if (sharedIps.Count > 0)
                factors.Add(new ScoreFactor("SharedIp", 15m, $"IP(s) {string.Join(", ", sharedIps)} shared (weak signal, never used alone)."));

            // never shared-IP alone: require >= 2 distinct signal types, at least one stronger than IP
            var nonIpFactors = factors.Count(f => f.Signal != "SharedIp");
            if (factors.Count < 2 || nonIpFactors == 0) continue;

            var score = Math.Min(95m, factors.Sum(f => f.Weight));
            if (score < Threshold) continue;

            yield return new RiskCase
            {
                CaseKey = $"{RuleId}:group:{group.Key}",
                RuleId = RuleId, RuleVersion = Version,
                Title = $"Coordinated activity across {accountIds.Count} accounts (group {group.Key})",
                Severity = score >= 60m ? RiskSeverity.High : RiskSeverity.Medium,
                Confidence = 0.78m, Score = score, ScoreBreakdown = factors,
                BusinessImpactUsd = Math.Round((accountIds.Count - 1) *
                    (s.Orders.Count > 0 ? s.Orders.Average(o => o.ListPrice) : 199m), 2),
                EntityRef = $"group:{group.Key}",
                Expected = "Independent accounts do not share devices, payment discounts and sessions.",
                Actual = $"Accounts {string.Join(", ", accountIds.OrderBy(x => x))} corroborate on {factors.Count} signals.",
                Evidence = access.Take(6).Select(a => Ev("AccessEvent", a.AccessId.ToString(), $"Account {a.AccountId} via {a.DeviceId}", $"ip={a.IpAddress}", a.OccurredUtc)).ToList()
            };
        }
    }
}
