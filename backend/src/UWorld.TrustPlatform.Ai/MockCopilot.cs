using System.Text;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Ai;

/// <summary>
/// Deterministic stand-in for an approved enterprise LLM. Produces the same structured,
/// cited output shape a real model would, built only from evidence already on the case, so the
/// demo is reproducible and credential-free. Swap in EnterpriseLlmCopilot behind ICopilot later.
/// </summary>
public sealed class MockCopilot : ICopilot
{
    private readonly ISignalRedactor _redactor;
    private readonly IPromptInjectionGuard _guard;

    public MockCopilot(ISignalRedactor redactor, IPromptInjectionGuard guard)
    {
        _redactor = redactor;
        _guard = guard;
    }

    public CopilotSummary Summarise(RiskCase c)
    {
        // Treat every evidence value as untrusted data: neutralize injection content, then redact.
        var injectionMatches = 0;
        var redacted = c.Evidence.Select(e =>
        {
            var r = _redactor.Redact(e);
            var inspectLabel = _guard.Inspect(r.Label);
            var inspectDetail = _guard.Inspect(r.Detail);
            injectionMatches += inspectLabel.MatchedPatterns.Count + inspectDetail.MatchedPatterns.Count;
            return new CaseEvidence
            {
                EvidenceId = r.EvidenceId, CaseId = r.CaseId, SourceType = r.SourceType, SourceId = r.SourceId,
                Label = inspectLabel.Neutralized, Detail = inspectDetail.Neutralized, OccurredUtc = r.OccurredUtc
            };
        }).ToList();

        string Clean(string? s) => _guard.Inspect(_redactor.Redact(s ?? "n/a")).Neutralized;

        var narrative = new StringBuilder()
            .Append($"Case {c.CaseId} was raised by the '{c.RuleId}' rule ({c.RuleVersion}) at {c.CreatedUtc:u}. ")
            .Append($"Expected behaviour: {Clean(c.Expected)} ")
            .Append($"Observed instead: {Clean(c.Actual)} ")
            .Append($"The weighted score is {c.Score} ({c.Severity}), driven by {c.ScoreBreakdown.Count} signal(s).")
            .ToString();

        var whyFlagged = c.ScoreBreakdown
            .Select(f => $"{f.Signal} (+{f.Weight}): {Clean(f.Rationale)}")
            .ToList();

        var cited = redacted
            .Select(e => new CitedEvidence(e.EvidenceId.ToString(), e.SourceType, e.Label))
            .ToList();

        return new CopilotSummary(
            Headline: Clean(c.Title),
            Narrative: narrative,
            WhyFlagged: whyFlagged,
            CitedEvidence: cited,
            UncertaintyLabels: BuildUncertainty(c, injectionMatches),
            RecommendedChecks: BuildChecks(c.RuleId),
            Disclaimer: "Advisory only. The AI investigates and explains; it never enforces access or changes accounts or payments. A human decides.",
            InjectionNeutralized: injectionMatches > 0,
            InjectionPatternsMatched: injectionMatches);
    }

    private static IReadOnlyList<string> BuildUncertainty(RiskCase c, int injectionMatches)
    {
        var labels = new List<string>();
        if (injectionMatches > 0)
            labels.Add($"⚠ {injectionMatches} prompt-injection pattern(s) in the source data were treated as data and neutralized before analysis.");
        if (c.Confidence < 0.85m) labels.Add($"Confidence {c.Confidence:P0}: corroborate before acting.");
        if (c.Evidence.Count < 2) labels.Add("Single evidence item - seek a second independent signal.");
        if (c.RuleId == "coordinated-account") labels.Add("Shared network can be benign (office/campus/VPN); weighted on multiple signals, never IP alone.");
        if (labels.Count == 0) labels.Add("High confidence; evidence is internally consistent.");
        return labels;
    }

    private static IReadOnlyList<string> BuildChecks(string ruleId) => ruleId switch
    {
        "authorization-gap" => new[] { "Confirm the response body size against the CDN/egress log for this correlation id.", "Verify the authorization middleware short-circuited before content assembly.", "Check whether other requests on this session share the pattern." },
        "entitlement-leakage" => new[] { "Re-evaluate entitlement at the exact access timestamp.", "Confirm the refund/expiry propagated to the access-check cache.", "Scan for other access events in the same lapsed window." },
        "revenue-abuse" => new[] { "Trace the order-to-payment reconciliation for this order.", "Check the discount code's intended single-use constraint.", "Look for related accounts sharing payment instruments." },
        "content-scraping" => new[] { "Inspect user-agent and request cadence for automation markers.", "Compare against the account's historical study pattern.", "Confirm whether rate limiting was active for this session." },
        "coordinated-account" => new[] { "Compare device fingerprints and payment instruments across the group.", "Confirm the shared IP is not a known corporate/campus egress.", "Review signup timing and email patterns." },
        "cve-exposure" => new[] { "Confirm the asset inventory version is current (not stale).", "Check whether a mitigating configuration is already in place.", "Prioritise patching against the CVE's exploitability." },
        "release-regression" => new[] { "Diff authorization config between the two release versions.", "Correlate the denial spike with specific endpoints.", "Check whether a rollback restores the baseline." },
        _ => new[] { "Review the linked evidence and corroborate with a second source." }
    };
}
