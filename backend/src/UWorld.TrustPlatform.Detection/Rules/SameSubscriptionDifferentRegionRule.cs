using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// One subscription accessed from two or more countries inside a short window — impossible travel /
/// account sharing. Uses USER_LOGS geo. Shared single-country access (campus/office) is never flagged.
/// </summary>
public sealed class SameSubscriptionDifferentRegionRule : IDetectionRule
{
    public string RuleId => "same-subscription-region";
    public string Version => "v1";
    public string Category => "Account";

    private readonly RuleConfigStore _config;
    public SameSubscriptionDifferentRegionRule(RuleConfigStore config) => _config = config;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var window = TimeSpan.FromHours(_config.Num(RuleId, "windowHours", 24));
        var minCountries = _config.Int(RuleId, "minCountries", 2);

        foreach (var g in s.UserLogs.Where(l => l.SubscriptionId is not null && l.CountryCode is not null)
                                    .GroupBy(l => l.SubscriptionId!.Value))
        {
            var logs = g.OrderBy(l => l.DateCreated).ToList();
            // find a window containing >= minCountries distinct countries
            for (var i = 0; i < logs.Count; i++)
            {
                var windowLogs = logs.Where(l => l.DateCreated >= logs[i].DateCreated
                                              && l.DateCreated <= logs[i].DateCreated + window).ToList();
                var countries = windowLogs.Select(l => l.CountryCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (countries.Count < minCountries) continue;

                var first = windowLogs.First(l => l.CountryCode == countries[0]);
                var other = windowLogs.First(l => l.CountryCode != countries[0]);
                var gapMin = Math.Abs((other.DateCreated - first.DateCreated).TotalMinutes);

                yield return new RiskCase
                {
                    CaseKey = $"{RuleId}:subscription:{g.Key}",
                    RuleId = RuleId, RuleVersion = Version,
                    Title = $"Subscription {g.Key} accessed from {countries.Count} countries ({string.Join(", ", countries)}) within {window.TotalHours:N0}h",
                    Severity = RiskSeverity.High, Confidence = 0.85m, Score = 80m,
                    BusinessImpactUsd = 0m,
                    ScoreBreakdown =
                    {
                        new ScoreFactor("DistinctRegions", 50m, $"{countries.Count} countries: {string.Join(", ", countries)}."),
                        new ScoreFactor("ImpossibleTravel", 30m, $"Access {gapMin:N0} min apart from different countries."),
                    },
                    AccountId = first.UserId, EntityRef = $"subscription:{g.Key}",
                    Expected = "A single subscription is used from one region at a time.",
                    Actual = $"Subscription {g.Key} · user {first.UserId} · accessed from {first.CountryCode} (ip {first.IPAddress}) at {first.DateCreated:u} and {other.CountryCode} (ip {other.IPAddress}) at {other.DateCreated:u} — {gapMin:N0} min apart, within {window.TotalHours:N0}h.",
                    Evidence =
                    {
                        Ev("UserLog", first.Id.ToString(), $"Access from {first.CountryCode}", $"ip={first.IPAddress}", first.DateCreated),
                        Ev("UserLog", other.Id.ToString(), $"Access from {other.CountryCode}", $"ip={other.IPAddress}", other.DateCreated),
                    }
                };
                break;   // one case per subscription
            }
        }
    }
}
