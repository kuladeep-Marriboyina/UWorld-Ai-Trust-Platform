namespace UWorld.TrustPlatform.Domain;

/// <summary>
/// One row in the request/cost ledger. Every HTTP call, ingestion pass, raised case and AI
/// investigation shares a RequestId, and each row carries both the AI spend it incurred and the
/// business dollars it put at risk - so cost rolls up per request, per rule, or per day.
/// </summary>
public sealed class LedgerEntry
{
    public long LedgerId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;       // HttpRequest / IngestionPass / CaseRaised / AiInvestigation
    public string Operation { get; set; } = string.Empty;
    public int? CaseId { get; set; }
    public string? RuleId { get; set; }
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal AiCostUsd { get; set; }
    public decimal BusinessImpactUsd { get; set; }
    public int DurationMs { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}

public sealed record UsageSummary(
    int Requests,
    int AiInvestigations,
    long TotalInputTokens,
    long TotalOutputTokens,
    decimal TotalAiCostUsd,
    decimal TotalBusinessImpactUsd,
    IReadOnlyList<RuleCost> ByRule);

public sealed record RuleCost(string RuleId, int Cases, decimal AiCostUsd, decimal BusinessImpactUsd);
