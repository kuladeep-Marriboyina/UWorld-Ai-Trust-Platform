using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

/// <summary>
/// A deterministic, versioned rule. Evaluate is pure over the signal set and must be
/// idempotent: the same signals always produce cases with the same CaseKey, so re-runs
/// never create duplicates.
/// </summary>
public interface IDetectionRule
{
    string RuleId { get; }
    string Version { get; }
    string Category { get; }

    IEnumerable<RiskCase> Evaluate(SignalSet signals);
}
