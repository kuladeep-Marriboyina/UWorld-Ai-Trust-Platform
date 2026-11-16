using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Matches a disclosed CVE to a live asset running the affected component+version, so exposure
/// is found before the exploitation window opens rather than after.
/// </summary>
public sealed class CveExposureRule : IDetectionRule
{
    public string RuleId => "cve-exposure";
    public string Version => "v1";
    public string Category => "Security";

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        foreach (var cve in s.Cves)
        {
            foreach (var asset in s.Assets)
            {
                if (!string.Equals(asset.Component, cve.Component, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(asset.Version, cve.AffectedVersion, StringComparison.OrdinalIgnoreCase)) continue;

                var severity = cve.Severity switch
                {
                    "Critical" => RiskSeverity.Critical,
                    "High" => RiskSeverity.High,
                    "Medium" => RiskSeverity.Medium,
                    _ => RiskSeverity.Low
                };

                yield return new RiskCase
                {
                    CaseKey = $"{RuleId}:{cve.CveId}:{asset.AssetId}",
                    RuleId = RuleId, RuleVersion = Version,
                    Title = $"{cve.CveId} ({cve.Severity}) affects {asset.Name}",
                    Severity = severity, Confidence = 0.95m,
                    Score = severity == RiskSeverity.Critical ? 88m : severity == RiskSeverity.High ? 74m : 55m,
                    ScoreBreakdown = { new ScoreFactor("CveMatch", 100m, $"{cve.Component} {asset.Version} matches {cve.CveId} affected range.") },
                    EntityRef = $"asset:{asset.AssetId}",
                    Expected = $"No live asset should run {cve.Component} {cve.AffectedVersion}.",
                    Actual = $"{asset.Name} runs {asset.Component} {asset.Version}, affected by {cve.CveId}.",
                    Evidence =
                    {
                        Ev("Cve", cve.CveId, $"{cve.CveId} {cve.Severity}", $"affects {cve.Component} {cve.AffectedVersion}", cve.PublishedUtc),
                        Ev("Asset", asset.AssetId.ToString(), $"{asset.Name} runs {asset.Component} {asset.Version}"),
                    }
                };
            }
        }
    }
}
