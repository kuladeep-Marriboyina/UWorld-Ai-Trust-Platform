using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Access (or payment) from a blocked IP or an embargoed country. The infrastructure already
/// captures BLOCKED_IPS and country embargo flags; this rule is the detection layer that was missing.
/// </summary>
public sealed class BlockedIpEmbargoRule : IDetectionRule
{
    public string RuleId => "blocked-ip-embargo";
    public string Version => "v1";
    public string Category => "Security";

    private readonly RuleConfigStore _config;
    public BlockedIpEmbargoRule(RuleConfigStore config) => _config = config;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var blocked = s.BlockedIps.Select(b => b.IPAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var blockedReason = s.BlockedIps.ToDictionary(b => b.IPAddress, b => b.Reason, StringComparer.OrdinalIgnoreCase);
        var embargoed = s.Countries.Where(c => c.IsEmbargoed).Select(c => c.IsoCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var l in s.UserLogs)
        {
            if (blocked.Contains(l.IPAddress))
                yield return Make($"blocked:{l.Id}", RiskSeverity.High, 78m,
                    $"Access from blocked IP {l.IPAddress}",
                    "No access should originate from a blocked IP.",
                    $"User {l.UserId} · subscription {l.SubscriptionId} · accessed from blocked IP {l.IPAddress} ({blockedReason.GetValueOrDefault(l.IPAddress)}) at {l.DateCreated:u}.",
                    l.UserId, Ev("UserLog", l.Id.ToString(), $"Blocked IP {l.IPAddress}", $"user={l.UserId}; sub={l.SubscriptionId}; reason={blockedReason.GetValueOrDefault(l.IPAddress)}", l.DateCreated));

            if (l.CountryCode is not null && embargoed.Contains(l.CountryCode))
                yield return Make($"embargo:{l.Id}", RiskSeverity.Medium, 60m,
                    $"Access from embargoed country {l.CountryCode}",
                    "Access must not originate from an embargoed country.",
                    $"User {l.UserId} · subscription {l.SubscriptionId} · accessed from embargoed country {l.CountryCode} (ip {l.IPAddress}) at {l.DateCreated:u}.",
                    l.UserId, Ev("UserLog", l.Id.ToString(), $"Embargoed country {l.CountryCode}", $"user={l.UserId}; sub={l.SubscriptionId}; ip={l.IPAddress}", l.DateCreated));
        }
    }

    private RiskCase Make(string key, RiskSeverity sev, decimal score, string title, string expected, string actual, int userId, CaseEvidence ev) => new()
    {
        CaseKey = $"{RuleId}:{key}", RuleId = RuleId, RuleVersion = Version,
        Title = title, Severity = sev, Confidence = 0.9m, Score = score, BusinessImpactUsd = 0m,
        ScoreBreakdown = { new ScoreFactor("AccessControl", score, actual) },
        AccountId = userId, EntityRef = "access",
        Expected = expected, Actual = actual, Evidence = { ev }
    };
}
