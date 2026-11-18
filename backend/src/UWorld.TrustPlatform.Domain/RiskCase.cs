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

    /// <summary>Business category (Account, Payment, Discount, Account Takeover…).</summary>
    public string? Category { get; set; }

    /// <summary>Every concrete thing this case touches (ip, device, user, email, card, code…) — powers linking.</summary>
    public List<CaseEntity> Entities { get; set; } = new();

    /// <summary>Recommended containment steps, specific to this case's entities.</summary>
    public List<string> Playbook { get; set; } = new();

    /// <summary>Exactly how BusinessImpactUsd was computed, with the numbers plugged in.</summary>
    public string? ImpactFormula { get; set; }

    /// <summary>Earliest / latest evidence time: the attack window. Latest = the event that tipped the score.</summary>
    public DateTime? FirstSignalUtc { get; set; }
    public DateTime? TriggerSignalUtc { get; set; }

    /// <summary>CreatedUtc − TriggerSignalUtc when detected live (null when found on backfill).</summary>
    public int? DetectionLatencyMs { get; set; }

    public DateTime? UpdatedUtc { get; set; }
    public DateTime? AcknowledgedUtc { get; set; }
    public string? AcknowledgedBy { get; set; }

    /// <summary>Analyst verdict: ConfirmedFraud / FalsePositive / Benign. Feeds rule precision.</summary>
    public string? Disposition { get; set; }
    public string? DispositionBy { get; set; }

    /// <summary>List views only: how much evidence is attached without loading it.</summary>
    public int EvidenceCount { get; set; }
}

/// <summary>What persisting a detected case did: Created, Updated (grew), Reopened, or Unchanged.</summary>
public sealed record CaseSaveOutcome(int CaseId, string Kind, string? Change, RiskCase Case);

public sealed record CaseEntity(string Type, string Value);

public static class Dispositions
{
    public const string ConfirmedFraud = "ConfirmedFraud";
    public const string FalsePositive = "FalsePositive";
    public const string Benign = "Benign";
    public static readonly string[] All = { ConfirmedFraud, FalsePositive, Benign };
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

public static class Durations
{
    /// <summary>"44 ms", "2.3s", "4 min" — detection latency in the unit a person would say out loud.</summary>
    public static string Short(int ms) => ms < 1000 ? $"{ms} ms" : ms < 90_000 ? $"{ms / 1000.0:0.#}s" : $"{ms / 60000.0:0} min";
}
