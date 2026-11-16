using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Ai;

/// <summary>Structured, cited output from the investigation copilot. The copilot only explains - it never enforces.</summary>
public sealed record CopilotSummary(
    string Headline,
    string Narrative,
    IReadOnlyList<string> WhyFlagged,
    IReadOnlyList<CitedEvidence> CitedEvidence,
    IReadOnlyList<string> UncertaintyLabels,
    IReadOnlyList<string> RecommendedChecks,
    string Disclaimer,
    bool InjectionNeutralized,
    int InjectionPatternsMatched);

public sealed record CitedEvidence(string EvidenceId, string SourceType, string Label);

public interface ICopilot
{
    /// <summary>Produces an advisory summary for a case. Implementations must route signals through ISignalRedactor first.</summary>
    CopilotSummary Summarise(RiskCase riskCase);
}
