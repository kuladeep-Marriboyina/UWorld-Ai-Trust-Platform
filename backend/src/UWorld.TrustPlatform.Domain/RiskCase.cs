namespace UWorld.TrustPlatform.Domain;

public sealed class RiskCase
{
    public int CaseId { get; set; }

    /// <summary>Deterministic idempotency key: ruleId:entity:windowStart.</summary>
    public string CaseKey { get; set; } = string.Empty;

    public string RuleId { get; set; } = string.Empty;
    public string RuleVersion { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public RiskSeverity Severity { get; set; }
    public decimal Confidence { get; set; }
    public decimal Score { get; set; }

    /// <summary>Estimated revenue-at-risk this case represents (0 for non-revenue rules like CVE/regression).</summary>
    public decimal BusinessImpactUsd { get; set; }

    /// <summary>Transparent per-signal contribution to the score (never a single opaque number).</summary>
    public List<ScoreFactor> ScoreBreakdown { get; set; } = new();

    public CaseStatus Status { get; set; } = CaseStatus.New;
    public int? AccountId { get; set; }
    public string? EntityRef { get; set; }

    /// <summary>What policy says should have happened.</summary>
    public string? Expected { get; set; }
    /// <summary>What actually happened in the data.</summary>
    public string? Actual { get; set; }

    public string? AssignedTo { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedUtc { get; set; }

    public List<CaseEvidence> Evidence { get; set; } = new();
    public List<CaseActivity> Activity { get; set; } = new();
}

public sealed record ScoreFactor(string Signal, decimal Weight, string Rationale);

public sealed class CaseEvidence
{
    public int EvidenceId { get; set; }
    public int CaseId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime? OccurredUtc { get; set; }
}

public sealed class CaseActivity
{
    public int ActivityId { get; set; }
    public int CaseId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}

public sealed record DetectionRuleDefinition(
    string RuleId, string Name, string Version, string Category, string Description, bool Enabled);
