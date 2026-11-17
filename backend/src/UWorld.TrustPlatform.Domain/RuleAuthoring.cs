namespace UWorld.TrustPlatform.Domain;

/// <summary>Runtime config row for a rule (from RULE_DEFINITIONS), shared by Data/Detection/Api.</summary>
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
}

/// <summary>A field the AI can target when mapping plain-English rules to the schema.</summary>
public sealed record SchemaField(string Table, string Field, string Type, string[] Synonyms, string? Note = null);

public sealed class SchemaCatalog
{
    public IReadOnlyList<SchemaField> Fields { get; init; } = Array.Empty<SchemaField>();
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A rule authored in plain English and compiled to a declarative form the engine runs with no
/// redeploy. Single-collection predicate model: iterate <see cref="Target"/>, AND the conditions,
/// raise a case per matching row.
/// </summary>
public sealed class DeclarativeRule
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Custom";
    public string Severity { get; set; } = "Medium";
    public string Target { get; set; } = string.Empty;              // e.g. USER_PAYMENTS, USER_LOGS
    public List<RuleCondition> When { get; set; } = new();          // ANDed
    public string? ImpactField { get; set; }                        // numeric field → business impact
    public string EnglishSource { get; set; } = string.Empty;
    public string Author { get; set; } = "analyst";
}

public sealed class RuleCondition
{
    public string Field { get; set; } = string.Empty;
    public string Op { get; set; } = "eq";     // eq ne gt lt gte lte in notin contains
    public string? Value { get; set; }
    public string[]? Values { get; set; }
}

/// <summary>Result of interpreting an English rule: a proposed rule, the field mappings inferred,
/// and clarifying questions the analyst must answer when a scenario is ambiguous.</summary>
public sealed record RuleInterpretation(
    DeclarativeRule? ProposedRule,
    IReadOnlyList<FieldMapping> FieldMappings,
    IReadOnlyList<string> ClarifyingQuestions,
    double Confidence,
    string Explanation);

public sealed record FieldMapping(string Phrase, string Table, string Field, string Interpretation);
