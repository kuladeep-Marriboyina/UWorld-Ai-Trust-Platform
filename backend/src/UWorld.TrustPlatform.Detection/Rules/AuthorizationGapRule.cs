using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// The core differentiator: a bare status code never tells us whether protected content
/// actually left the building. We flag access events that returned bytes even though the
/// correlated authorization outcome was Denied or Missing.
/// </summary>
public sealed class AuthorizationGapRule : IDetectionRule
{
    public string RuleId => "authorization-gap";
    public string Version => "v1";
    public string Category => "Access";

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var authByCorrelation = s.AuthOutcomes
            .GroupBy(a => a.CorrelationId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var access in s.AccessEvents)
        {
            if (access.ContentReturnedBytes <= 0) continue;
            if (!authByCorrelation.TryGetValue(access.CorrelationId, out var auth)) continue;

            var denied = auth.AuthResult is "Denied" or "Missing";
            if (!denied) continue;

            yield return new RiskCase
            {
                CaseKey = $"{RuleId}:access:{access.AccessId}",
                RuleId = RuleId,
                RuleVersion = Version,
                Title = $"Protected content returned after {auth.AuthResult} authorization",
                Severity = RiskSeverity.Critical,
                Confidence = 0.97m,
                Score = 96m,
                BusinessImpactUsd = EstimateContentValue(s, access.PackageId),
                ScoreBreakdown =
                {
                    new ScoreFactor("AuthResult", 60m, $"Authorization was {auth.AuthResult}."),
                    new ScoreFactor("ContentReturnedBytes", 36m, $"{access.ContentReturnedBytes:N0} bytes of protected content were returned anyway."),
                },
                AccountId = access.AccountId,
                EntityRef = access.Resource,
                Expected = $"A {auth.AuthResult} authorization must return 0 bytes of protected content.",
                Actual = $"AuthResult={auth.AuthResult} yet {access.ContentReturnedBytes:N0} bytes were returned for {access.Resource}.",
                Evidence =
                {
                    Ev("AuthOutcome", auth.AuthId.ToString(), $"Authorization {auth.AuthResult} for {auth.Resource}", $"correlationId={auth.CorrelationId}", auth.OccurredUtc),
                    Ev("AccessEvent", access.AccessId.ToString(), $"{access.ContentReturnedBytes:N0} bytes returned for {access.Resource}", $"session={access.SessionId}; correlationId={access.CorrelationId}", access.OccurredUtc),
                }
            };
        }
    }

    /// <summary>Value of the protected content exposed, approximated by the package's list price.</summary>
    private static decimal EstimateContentValue(SignalSet s, int? packageId)
    {
        var prices = s.Orders.Where(o => packageId is null || o.PackageId == packageId).Select(o => o.ListPrice).ToList();
        return prices.Count > 0 ? Math.Round(prices.Average(), 2) : 199m;
    }
}
