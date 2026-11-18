using System.Text.RegularExpressions;

namespace UWorld.TrustPlatform.Detection;

/// <summary>What a set of email addresses says about whether one actor created them.</summary>
public sealed record EmailPatternReport(
    int Total,
    string? DominantTemplate,          // e.g. "studyhub.acc##@mailinator.com"
    int TemplateShare,                 // how many addresses fit the dominant template
    bool Sequential,                   // numbered 01, 02, 03…
    int SameInboxCount,                // plus-aliases / gmail dot-variants of one mailbox
    string? SameInbox,
    int DisposableCount,
    IReadOnlyList<string> DisposableDomains,
    int RandomizedCount);

/// <summary>
/// Deterministic identity heuristics. Each one is explainable in a sentence, which is the point:
/// an analyst must be able to read exactly why accounts were linked.
/// </summary>
public static partial class IdentityAnalysis
{
    public static readonly HashSet<string> DisposableDomainList = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "guerrillamail.com", "10minutemail.com", "tempmail.com", "temp-mail.org", "yopmail.com",
        "trashmail.com", "sharklasers.com", "getnada.com", "dispostable.com", "maildrop.cc", "throwawaymail.com",
        "fakeinbox.com", "mintemail.com", "tempinbox.com", "emailondeck.com", "mohmal.com", "burnermail.io"
    };

    private static readonly HashSet<string> DotInsensitive = new(StringComparer.OrdinalIgnoreCase) { "gmail.com", "googlemail.com" };

    [GeneratedRegex(@"\d+")] private static partial Regex Digits();
    [GeneratedRegex(@"(\d+)$")] private static partial Regex TrailingNumber();
    [GeneratedRegex(@"[._\-]+$")] private static partial Regex TrailingSeparators();
    [GeneratedRegex(@"(python-requests|curl/|wget|headless|phantomjs|selenium|puppeteer|playwright|httpclient|okhttp|go-http-client|scrapy|bot\b)", RegexOptions.IgnoreCase)]
    private static partial Regex AutomationUa();

    public static (string Local, string Domain) Split(string email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        var at = e.LastIndexOf('@');
        return at < 0 ? (e, "") : (e[..at], e[(at + 1)..]);
    }

    public static string Domain(string email) => Split(email).Domain;

    /// <summary>The real mailbox: strips +tags and, for Gmail, dots (john.doe+1@gmail.com == johndoe@gmail.com).</summary>
    public static string Mailbox(string email)
    {
        var (local, domain) = Split(email);
        var plus = local.IndexOf('+');
        if (plus >= 0) local = local[..plus];
        if (DotInsensitive.Contains(domain)) local = local.Replace(".", "");
        return $"{local}@{domain}";
    }

    /// <summary>Digit runs collapsed: "john.doe17" → "john.doe#". Template = skeleton + domain.</summary>
    public static string Skeleton(string email)
    {
        var (local, _) = Split(email);
        var plus = local.IndexOf('+');
        if (plus >= 0) local = local[..plus];
        local = Digits().Replace(local, "#");
        return local;
    }

    /// <summary>The stem a human would recognise: digits and trailing separators removed ("acc.07" → "acc").</summary>
    public static string Stem(string email)
    {
        var (local, _) = Split(email);
        var plus = local.IndexOf('+');
        if (plus >= 0) local = local[..plus];
        local = Digits().Replace(local, "");
        return TrailingSeparators().Replace(local, "");
    }

    public static bool IsDisposable(string email) => DisposableDomainList.Contains(Domain(email));

    public static bool IsAutomationClient(string? userAgent) => !string.IsNullOrEmpty(userAgent) && AutomationUa().IsMatch(userAgent);

    /// <summary>Machine-generated local parts: long, digit-heavy, few vowels (e.g. "xk3j9q2zt7").</summary>
    public static bool LooksRandomized(string email)
    {
        var (local, _) = Split(email);
        if (local.Length < 8) return false;
        var digits = local.Count(char.IsDigit);
        var letters = local.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return digits >= 8;
        var vowels = letters.Count(c => "aeiou".Contains(c));
        var switches = 0;
        for (var i = 1; i < local.Length; i++) if (char.IsDigit(local[i]) != char.IsDigit(local[i - 1])) switches++;
        return digits >= 3 && switches >= 4 && vowels / (double)letters.Count < 0.3;
    }

    public static EmailPatternReport Analyze(IReadOnlyCollection<string> emails)
    {
        var list = emails.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        if (list.Count == 0) return new EmailPatternReport(0, null, 0, false, 0, null, 0, Array.Empty<string>(), 0);

        // templates: same stem (≥4 chars) — "jake.miller01", "jake.miller02" → "jake.miller"
        var byStem = list.GroupBy(Stem).Where(g => g.Key.Length >= 4).OrderByDescending(g => g.Count()).FirstOrDefault();
        string? template = null; var share = 0; var sequential = false;
        if (byStem is not null && byStem.Count() >= 2)
        {
            share = byStem.Count();
            var sample = byStem.First();
            var domains = byStem.Select(Domain).Distinct().ToList();
            template = Skeleton(sample).Replace("#", "##") + "@" + (domains.Count == 1 ? domains[0] : "*");
            var nums = byStem.Select(e => TrailingNumber().Match(Split(e).Local.Split('+')[0]))
                             .Where(m => m.Success).Select(m => long.TryParse(m.Value, out var n) ? n : -1).Where(n => n >= 0)
                             .Distinct().OrderBy(n => n).ToList();
            if (nums.Count >= 2)
            {
                var consecutive = 0;
                for (var i = 1; i < nums.Count; i++) if (nums[i] - nums[i - 1] <= 2) consecutive++;
                sequential = consecutive >= Math.Max(1, nums.Count - 2);
            }
        }

        var inbox = list.GroupBy(Mailbox).OrderByDescending(g => g.Count()).First();
        var disposable = list.Where(IsDisposable).ToList();

        return new EmailPatternReport(
            list.Count, template, share, sequential,
            inbox.Count() >= 2 ? inbox.Count() : 0, inbox.Count() >= 2 ? inbox.Key : null,
            disposable.Count, disposable.Select(Domain).Distinct().ToList(),
            list.Count(LooksRandomized));
    }

    /// <summary>Partially masks an address for AI prompts / shared reports while keeping its shape visible.</summary>
    public static string Mask(string email)
    {
        var (local, domain) = Split(email);
        if (local.Length <= 3) return $"{local[..Math.Min(1, local.Length)]}***@{domain}";
        var tail = TrailingNumber().Match(local);
        var keep = local[..Math.Min(4, local.Length)];
        return $"{keep}***{(tail.Success ? tail.Value : "")}@{domain}";
    }
}
