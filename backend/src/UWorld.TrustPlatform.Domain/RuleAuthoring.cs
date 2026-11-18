namespace UWorld.TrustPlatform.Domain;

/// <summary>Runtime config + governance row for a rule (RuleDefinitions), shared by Data/Detection/Api.</summary>
public sealed class RuleConfigEntry
{
    public string RuleId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? Severity { get; set; }
    public bool Enabled { get; set; } = true;
    public string? ConfigJson { get; set; }
    public bool IsDeclarative { get; set; }
    public string? DeclarativeJson { get; set; }
    public string? Author { get; set; }

    // governance
    public string Status { get; set; } = RuleStatus.Active;
    public string? Owner { get; set; }
    public int VersionNo { get; set; } = 1;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public string? Rationale { get; set; }
    public string? PlaybookJson { get; set; }
    public int? PendingVersionNo { get; set; }
    public DateTime? CreatedUtc { get; set; }
    public DateTime? DateUpdated { get; set; }

    /// <summary>Only Active + Enabled rules run in the engine.</summary>
    public bool IsLive => Enabled && string.Equals(Status, RuleStatus.Active, StringComparison.OrdinalIgnoreCase);
}

public static class RuleStatus
{
    public const string Active = "Active";
    public const string PendingApproval = "PendingApproval";
    public const string Disabled = "Disabled";
    public const string Rejected = "Rejected";
}

/// <summary>One proposed or historical version of a rule (RuleVersions).</summary>
public sealed class RuleVersion
{
    public int Id { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public int VersionNo { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = "{}";
    public string? ChangeNote { get; set; }
    public string Author { get; set; } = string.Empty;
    public string? Reviewer { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ReviewedUtc { get; set; }
}

/// <summary>
/// The full, reviewable definition of a rule change. For coded rules only the tunables
/// (severity, ConfigJson parameters, playbook) change; for authored rules the whole detection logic lives in <see cref="Logic"/>.
/// </summary>
public sealed class RuleDefinitionDraft
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public string Severity { get; set; } = "Medium";
    public string Description { get; set; } = string.Empty;
    public string? Rationale { get; set; }
    public string? ConfigJson { get; set; }
    public List<string> Playbook { get; set; } = new();
    public DeclarativeRule? Logic { get; set; }
    public string? ChangeNote { get; set; }
}

/// <summary>A field the AI and the rule builder can target, with type, meaning and allowed values.</summary>
public sealed record SchemaField(string Table, string Field, string Type, string[] Synonyms, string? Note = null);

/// <summary>A signal table rules can run over: what one row means and which column orders it in time.</summary>
public sealed record SchemaTarget(string Table, string Description, string TimeField, string EntityLabel);

public sealed class SchemaCatalog
{
    public IReadOnlyList<SchemaField> Fields { get; init; } = Array.Empty<SchemaField>();
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<SchemaTarget> TargetInfo { get; init; } = Array.Empty<SchemaTarget>();
}

/// <summary>
/// An authored detection rule the engine runs with no redeploy.
///   1. take rows of <see cref="Target"/>
///   2. keep rows matching ALL <see cref="When"/> filters and NONE of the <see cref="Exclusions"/>
///   3a. no <see cref="GroupBy"/>: every remaining row raises a case
///   3b. with GroupBy: group rows by that field, slide a <see cref="WindowMinutes"/> window over each group,
///       compute <see cref="Aggregate"/> (count / distinct(field) / sum(field)) and raise a case when it meets the threshold.
/// </summary>
public sealed class DeclarativeRule
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public string Severity { get; set; } = "Medium";
    public string Target { get; set; } = string.Empty;
    public List<RuleCondition> When { get; set; } = new();
    public List<RuleCondition> Exclusions { get; set; } = new();

    public string? GroupBy { get; set; }
    public int? WindowMinutes { get; set; }
    public string Aggregate { get; set; } = "count";        // count | distinct | sum
    public string? AggregateField { get; set; }
    public string ThresholdOp { get; set; } = "gte";
    public decimal? ThresholdValue { get; set; }

    public string? ImpactField { get; set; }                // sum of this numeric field over the matched rows
    public decimal? ImpactPerMatchUsd { get; set; }         // or a flat $ per matched row
    public decimal Confidence { get; set; } = 0.8m;
    public string? Description { get; set; }
    public List<string> Playbook { get; set; } = new();
    public string EnglishSource { get; set; } = string.Empty;
    public string Author { get; set; } = "analyst";
}

public sealed class RuleCondition
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = "eq";     // eq ne gt lt gte lte in notin contains startswith
    public string? Value { get; set; }
    public string[]? Values { get; set; }

    public override string ToString() =>
        $"{Field} {Op} {(Values is { Length: > 0 } ? "[" + string.Join(", ", Values) + "]" : Value)}";
}

/// <summary>Result of interpreting an English rule: a proposed rule, the field mappings inferred,
/// and clarifying questions the author must answer when the intent is ambiguous.</summary>
public sealed record RuleInterpretation(
    DeclarativeRule? ProposedRule,
    IReadOnlyList<FieldMapping> FieldMappings,
    IReadOnlyList<string> ClarifyingQuestions,
    double Confidence,
    string Explanation,
    string Source = "deterministic",
    IReadOnlyList<string>? Assumptions = null);

public sealed record FieldMapping(string Phrase, string Table, string Field, string Interpretation);
