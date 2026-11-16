using System.Text.RegularExpressions;

namespace UWorld.TrustPlatform.Ai;

public sealed record InjectionInspection(bool Detected, IReadOnlyList<string> MatchedPatterns, string Neutralized);

/// <summary>
/// The copilot reads data from logs that an attacker can influence. This guard treats every such
/// value as untrusted DATA, never instructions: it detects known prompt-injection / jailbreak
/// patterns and neutralizes them before anything is handed to a model. Crucially, even a missed
/// injection cannot "hack" anything - the copilot has no tools that change access, accounts or
/// payments. This is defence in depth, not the only line of defence.
/// </summary>
public interface IPromptInjectionGuard
{
    InjectionInspection Inspect(string? text);
}

public sealed partial class PromptInjectionGuard : IPromptInjectionGuard
{
    // Instruction-override, exfiltration and destructive-intent patterns.
    private static readonly Regex[] Patterns =
    {
        BuildOverride(), BuildRoleplay(), BuildExfiltrate(), BuildDestructive(), BuildGrant(), BuildSystemPrompt()
    };

    [GeneratedRegex(@"ignore\s+(all\s+)?(previous|above|prior)\s+instructions|disregard\s+(the\s+)?(above|previous)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildOverride();

    [GeneratedRegex(@"you\s+are\s+now|pretend\s+to\s+be|act\s+as\s+(an?\s+)?(unrestricted|jailbroken|dan)|developer\s+mode", RegexOptions.IgnoreCase)]
    private static partial Regex BuildRoleplay();

    [GeneratedRegex(@"exfiltrat|send\s+(the\s+)?(secrets?|credentials?|connection\s+string)|reveal\s+(your\s+)?(system\s+)?prompt|print\s+(all\s+)?api\s+keys?", RegexOptions.IgnoreCase)]
    private static partial Regex BuildExfiltrate();

    [GeneratedRegex(@"drop\s+table|delete\s+from|truncate\s+table|xp_cmdshell|;\s*shutdown", RegexOptions.IgnoreCase)]
    private static partial Regex BuildDestructive();

    [GeneratedRegex(@"grant\s+(me\s+)?(admin|access|entitlement)|bypass\s+(the\s+)?(auth|authorization|entitlement)|escalate\s+privileges?", RegexOptions.IgnoreCase)]
    private static partial Regex BuildGrant();

    [GeneratedRegex(@"<\s*system\s*>|\[\s*system\s*\]|###\s*system", RegexOptions.IgnoreCase)]
    private static partial Regex BuildSystemPrompt();

    public InjectionInspection Inspect(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new InjectionInspection(false, Array.Empty<string>(), text ?? string.Empty);

        var matched = new List<string>();
        var neutralized = text;
        foreach (var p in Patterns)
        {
            if (!p.IsMatch(neutralized)) continue;
            matched.Add(p.ToString());
            neutralized = p.Replace(neutralized, "[neutralized-instruction]");
        }

        return new InjectionInspection(matched.Count > 0, matched, neutralized);
    }
}
