using System.Text.RegularExpressions;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Ai;

/// <summary>
/// Turns a plain-English rule into a declarative rule, mapping wording to real schema fields and
/// returning clarifying questions when a scenario is ambiguous. Pluggable (mirrors ICopilot) — this
/// deterministic mock runs credential-free; an approved enterprise LLM can slot in behind the interface.
/// </summary>
public interface IRuleInterpreter
{
    RuleInterpretation Interpret(string english, SchemaCatalog catalog, IReadOnlyDictionary<string, string>? answers = null);
}

public sealed partial class MockRuleInterpreter : IRuleInterpreter
{
    [GeneratedRegex(@"(?:over|more than|greater than|above|at least|>=?|exceeds?)\s*\$?\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"\b(\d+)\s*(?:times|uses|usages)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimesRegex();

    private static readonly Dictionary<string, string> CountryIso = new(StringComparer.OrdinalIgnoreCase)
    { ["iran"] = "IR", ["north korea"] = "KP", ["india"] = "IN", ["us"] = "US", ["usa"] = "US", ["united states"] = "US", ["uk"] = "GB", ["britain"] = "GB" };

    public RuleInterpretation Interpret(string english, SchemaCatalog catalog, IReadOnlyDictionary<string, string>? answers = null)
    {
        var t = (english ?? "").Trim();
        var lower = t.ToLowerInvariant();
        var maps = new List<FieldMapping>();
        var conds = new List<RuleCondition>();
        var questions = new List<string>();
        answers ??= new Dictionary<string, string>();

        // ---- pick target ----
        string target =
            Regex.IsMatch(lower, @"payment|order|paid|braintree|charge|refund") ? "USER_PAYMENTS" :
            Regex.IsMatch(lower, @"\blog|access|country|region|\bip\b|login") ? "USER_LOGS" :
            Regex.IsMatch(lower, @"code|discount|coupon|referral|promo") ? "NCLEX_REFERRAL_CODES" :
            Regex.IsMatch(lower, @"subscription") ? "USER_SUBSCRIPTIONS" : "USER_PAYMENTS";

        string? impactField = target == "USER_PAYMENTS" ? "FinalAmount" : target == "NCLEX_REFERRAL_CODES" ? "DiscountPercent" : null;

        if (target == "USER_PAYMENTS")
        {
            if (lower.Contains("paid")) { conds.Add(new RuleCondition { Field = "PaymentStatus", Op = "eq", Value = "1" }); maps.Add(new("paid", "USER_PAYMENTS", "PaymentStatus", "PaymentStatus = 1 (Paid)")); }
            if (Regex.IsMatch(lower, @"no|without|missing") && lower.Contains("braintree"))
            {
                conds.Add(new RuleCondition { Field = "TransactionId", Op = "in", Values = new[] { "0", "0000", "" } });
                maps.Add(new("no braintree transaction", "USER_PAYMENTS", "TransactionId", "TransactionId in {0,0000,''} (placeholder = no gateway txn)"));
            }
            var amt = AmountRegex().Match(lower);
            if (amt.Success) { conds.Add(new RuleCondition { Field = "FinalAmount", Op = "gt", Value = amt.Groups[1].Value }); maps.Add(new($"over ${amt.Groups[1].Value}", "USER_PAYMENTS", "FinalAmount", $"FinalAmount > {amt.Groups[1].Value}")); }
            else if (answers.TryGetValue("minAmount", out var ma) && decimal.TryParse(ma, out _)) { conds.Add(new RuleCondition { Field = "FinalAmount", Op = "gt", Value = ma }); maps.Add(new($"over ${ma}", "USER_PAYMENTS", "FinalAmount", $"FinalAmount > {ma}")); }
            else questions.Add("What minimum order amount should trigger this rule (e.g. $50)? Reply with a number as 'minAmount'.");

            if (lower.Contains("trial") || lower.Contains("store credit") || lower.Contains("$0"))
                { conds.Add(new RuleCondition { Field = "TransactionAmount", Op = "gt", Value = "0" }); maps.Add(new("excluding free trials", "USER_PAYMENTS", "TransactionAmount", "TransactionAmount > 0 (excludes $0 trials/credit)")); }
            else if (!answers.ContainsKey("excludeTrials"))
                questions.Add("Should $0 free-trial / store-credit orders be excluded? Reply 'yes' or 'no' as 'excludeTrials'.");
            else if (string.Equals(answers["excludeTrials"], "yes", StringComparison.OrdinalIgnoreCase))
                { conds.Add(new RuleCondition { Field = "TransactionAmount", Op = "gt", Value = "0" }); maps.Add(new("exclude trials (answered)", "USER_PAYMENTS", "TransactionAmount", "TransactionAmount > 0")); }
        }
        else if (target == "USER_LOGS")
        {
            var countryWord = Regex.Match(lower, @"(?:from|country)\s+([a-z ]{2,20})");
            var iso = countryWord.Success && CountryIso.TryGetValue(countryWord.Groups[1].Value.Trim(), out var c) ? c
                      : answers.GetValueOrDefault("country");
            if (iso is not null) { conds.Add(new RuleCondition { Field = "CountryCode", Op = "eq", Value = iso }); maps.Add(new($"country {iso}", "USER_LOGS", "CountryCode", $"CountryCode = {iso}")); }
            else questions.Add("Which country code should this match (e.g. IR)? Reply as 'country'.");
        }
        else if (target == "NCLEX_REFERRAL_CODES")
        {
            var times = TimesRegex().Match(lower);
            var n = times.Success ? times.Groups[1].Value : answers.GetValueOrDefault("threshold");
            if (n is not null) { conds.Add(new RuleCondition { Field = "ReferralCodeUsed", Op = "gt", Value = n }); maps.Add(new($"used more than {n} times", "NCLEX_REFERRAL_CODES", "ReferralCodeUsed", $"ReferralCodeUsed > {n}")); }
            else questions.Add("Above how many uses should a code be flagged? Reply with a number as 'threshold'.");
        }

        var severity = Regex.IsMatch(lower, @"critical|fraud|stolen|urgent") ? "High" : "Medium";
        var name = BuildName(t);
        var ruleId = Slug(name);

        DeclarativeRule? proposed = conds.Count > 0 && questions.Count == 0
            ? new DeclarativeRule { RuleId = ruleId, Name = name, Category = "Custom", Severity = severity, Target = target, When = conds, ImpactField = impactField, EnglishSource = t, Author = "analyst" }
            : conds.Count > 0
                ? new DeclarativeRule { RuleId = ruleId, Name = name, Category = "Custom", Severity = severity, Target = target, When = conds, ImpactField = impactField, EnglishSource = t, Author = "analyst" }
                : null;

        var confidence = questions.Count == 0 && conds.Count > 0 ? 0.9 : conds.Count > 0 ? 0.6 : 0.3;
        var explanation = conds.Count > 0
            ? $"Interpreted as a rule over {target} with {conds.Count} condition(s)." + (questions.Count > 0 ? " Needs clarification before it can go live." : " Ready to activate.")
            : "Could not confidently map this to the schema — please clarify.";

        return new RuleInterpretation(proposed, maps, questions, confidence, explanation);
    }

    private static string BuildName(string text)
    {
        var words = Regex.Replace(text, @"[^\w ]", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(7);
        var n = string.Join(' ', words);
        return string.IsNullOrWhiteSpace(n) ? "Custom rule" : char.ToUpper(n[0]) + n[1..];
    }

    private static string Slug(string name) =>
        "nl-" + Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
}
