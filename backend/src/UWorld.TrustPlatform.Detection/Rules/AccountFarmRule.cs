using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>One group of linked new accounts and how risky it currently looks (even below the alert line).</summary>
public sealed record ClusterAssessment(
    int AnchorUserId,
    IReadOnlyList<UserRecord> Accounts,
    decimal Score,
    IReadOnlyList<ScoreFactor> Factors,
    bool Corroborated,              // at least one identity signal beyond a shared network
    bool WouldAlert,
    string PrimaryIp,
    IReadOnlyList<string> Ips,
    IReadOnlyList<string> Devices,
    IReadOnlyList<string> Cards,
    EmailPatternReport Emails,
    DateTime FirstUtc,
    DateTime LastUtc,
    int SubscriptionsTaken,
    decimal UnpaidAccessUsd,
    string ImpactFormula);

/// <summary>
/// Account farm / multi-accounting. New accounts are linked when they share a registration IP inside a short
/// window, a device fingerprint, or a payment card. Each linked cluster is scored on independent, explainable
/// factors. A shared network alone never raises a case — campuses, offices and mobile carriers share IPs —
/// it needs corroboration from the identities themselves (templated or disposable emails, one device, one card,
/// automation clients).
/// </summary>
public sealed class AccountFarmRule : IDetectionRule
{
    public string RuleId => "account-farm";
    public string Version => "v2";
    public string Category => "Account";

    private readonly RuleConfigStore _config;
    public AccountFarmRule(RuleConfigStore config) => _config = config;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var minAccounts = _config.Int(RuleId, "minAccounts", 3);
        foreach (var c in Assess(s).Where(c => c.WouldAlert && c.Accounts.Count >= minAccounts))
            yield return ToCase(s, c);
    }

    /// <summary>All clusters of ≥2 linked recent accounts, scored. Used for cases, the live "emerging risk" view and signup previews.</summary>
    public IReadOnlyList<ClusterAssessment> Assess(SignalSet s)
    {
        var window = TimeSpan.FromMinutes(_config.Num(RuleId, "windowMinutes", 60));
        var lookback = TimeSpan.FromHours(_config.Num(RuleId, "lookbackHours", 168));
        var alertScore = (decimal)_config.Num(RuleId, "alertScore", 55);
        var minAccounts = _config.Int(RuleId, "minAccounts", 3);

        var users = s.Users.Where(u => u.DateCreated >= s.EvaluatedUtc - lookback).OrderBy(u => u.DateCreated).ToList();
        if (users.Count < 2) return Array.Empty<ClusterAssessment>();

        var ids = users.Select(u => u.UserId).ToHashSet();
        var parent = users.ToDictionary(u => u.UserId, u => u.UserId);
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb); }

        // link 1: same registration IP, chained within the window
        foreach (var g in users.GroupBy(u => u.RegistrationIp))
        {
            var ordered = g.OrderBy(u => u.DateCreated).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].DateCreated - ordered[i - 1].DateCreated <= window) Union(ordered[i - 1].UserId, ordered[i].UserId);
        }
        // link 2: same device fingerprint (any time in the lookback)
        foreach (var g in users.GroupBy(u => u.DeviceFingerprint).Where(g => g.Count() > 1))
        {
            var first = g.First().UserId;
            foreach (var u in g.Skip(1)) Union(first, u.UserId);
        }
        // link 3: same payment card
        var cardsByUser = s.Payments2.Where(p => p.CardFingerprint != null && ids.Contains(p.UserId))
            .GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.Select(p => p.CardFingerprint!).Distinct().ToList());
        foreach (var g in cardsByUser.SelectMany(kv => kv.Value.Select(card => (card, user: kv.Key))).GroupBy(x => x.card).Where(g => g.Count() > 1))
        {
            var first = g.First().user;
            foreach (var x in g.Skip(1)) Union(first, x.user);
        }

        var userLogsUa = s.ApiLogs.Where(l => l.UserId != null && ids.Contains(l.UserId.Value) && l.UserAgent != null)
            .GroupBy(l => l.UserId!.Value).ToDictionary(g => g.Key, g => g.Select(l => l.UserAgent!).Distinct().ToList());

        var result = new List<ClusterAssessment>();
        foreach (var cluster in users.GroupBy(u => Find(u.UserId)).Where(g => g.Count() >= 2))
        {
            var accts = cluster.OrderBy(u => u.DateCreated).ToList();
            var n = accts.Count;
            var factors = new List<ScoreFactor>();

            var ipGroups = accts.GroupBy(u => u.RegistrationIp).OrderByDescending(g => g.Count()).ToList();
            var topIp = ipGroups[0];
            if (topIp.Count() >= 2)
            {
                var span = topIp.Max(u => u.DateCreated) - topIp.Min(u => u.DateCreated);
                factors.Add(new ScoreFactor("SharedNetwork", Math.Min(25m, 10m + 4m * (topIp.Count() - 2)),
                    $"{topIp.Count()} accounts registered from {topIp.Key} within {Human(span)}. A shared network is weak on its own (campuses, offices, carriers)."));
            }

            var devGroups = accts.GroupBy(u => u.DeviceFingerprint).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count()).ToList();
            if (devGroups.Count > 0)
            {
                var d = devGroups[0];
                factors.Add(new ScoreFactor("SharedDevice", Math.Min(35m, 20m + 5m * (d.Count() - 2)),
                    $"{d.Count()} accounts created on the same device ({d.Key}). Real students rarely register several accounts on one device."));
            }

            var clusterCards = accts.SelectMany(u => cardsByUser.TryGetValue(u.UserId, out var c) ? c.Select(card => (card, u.UserId)) : Enumerable.Empty<(string, int)>())
                .GroupBy(x => x.Item1).Where(g => g.Select(x => x.Item2).Distinct().Count() >= 2).ToList();
            if (clusterCards.Count > 0)
            {
                var card = clusterCards[0];
                var last4 = s.Payments2.FirstOrDefault(p => p.CardFingerprint == card.Key)?.CardLast4;
                factors.Add(new ScoreFactor("SharedCard", 35m,
                    $"One payment card (•••• {last4 ?? "????"}) funds {card.Select(x => x.Item2).Distinct().Count()} different accounts."));
            }

            var emails = IdentityAnalysis.Analyze(accts.Select(u => u.Email).ToList());
            var templateNeeded = n <= 2 ? 2 : Math.Max(3, (int)Math.Ceiling(n * 0.5));
            if (emails.TemplateShare >= templateNeeded)
                factors.Add(new ScoreFactor("TemplatedEmails", emails.Sequential ? 30m : 25m,
                    $"{emails.TemplateShare} of {n} emails follow one template ({emails.DominantTemplate}){(emails.Sequential ? ", numbered in sequence" : "")}."));
            if (emails.SameInboxCount >= 2)
                factors.Add(new ScoreFactor("SameInbox", 25m,
                    $"{emails.SameInboxCount} addresses are aliases of one mailbox ({emails.SameInbox}) via +tags or Gmail dots."));
            if (emails.DisposableCount * 2 >= n)
                factors.Add(new ScoreFactor("DisposableEmail", 15m,
                    $"{emails.DisposableCount} of {n} use disposable email domains ({string.Join(", ", emails.DisposableDomains)})."));
            if (emails.RandomizedCount * 2 >= n && n >= 3)
                factors.Add(new ScoreFactor("GeneratedEmails", 15m, $"{emails.RandomizedCount} of {n} local parts look machine-generated."));

            var botUas = accts.SelectMany(u => (userLogsUa.TryGetValue(u.UserId, out var l) ? l : new List<string>()).Append(u.UserAgent ?? ""))
                .Where(IdentityAnalysis.IsAutomationClient).Distinct().ToList();
            if (botUas.Count > 0)
                factors.Add(new ScoreFactor("AutomationClient", 10m, $"Requests came from a scripted client ({botUas[0]}), not a browser or the app."));

            if (n >= 3)
            {
                var gaps = accts.Zip(accts.Skip(1), (a, b) => (b.DateCreated - a.DateCreated).TotalSeconds).OrderBy(x => x).ToList();
                var median = gaps[gaps.Count / 2];
                if (median <= 120)
                    factors.Add(new ScoreFactor("ScriptedCadence", 10m, $"Median gap between signups is {median:N0}s — faster than people fill a signup form."));
            }

            var subs = s.Subscriptions.Where(x => accts.Any(u => u.UserId == x.UserId) && x.DateActivated >= accts[0].DateCreated.AddMinutes(-5)).ToList();
            var userIds = accts.Select(u => u.UserId).ToHashSet();
            var pays = s.Payments2.Where(p => userIds.Contains(p.UserId) && p.DateCreated >= accts[0].DateCreated.AddMinutes(-5)).ToList();
            var listValue = subs.Sum(x => s.PriceOf(x.PackageId));
            var collected = pays.Sum(p => p.FinalAmount);
            var unpaid = Math.Max(0m, listValue - collected);
            var harvesters = subs.Select(x => x.UserId).Distinct().Count(uid => pays.Where(p => p.UserId == uid).Sum(p => p.FinalAmount) < subs.Where(x => x.UserId == uid).Sum(x => s.PriceOf(x.PackageId)));
            if (harvesters >= 2)
                factors.Add(new ScoreFactor("TrialHarvest", Math.Min(20m, 10m + 2m * (harvesters - 2)),
                    $"{harvesters} of these accounts hold free-trial or discounted subscriptions worth ${listValue:N0} list, ${collected:N0} collected."));

            var corroborated = factors.Any(f => f.Signal is "SharedDevice" or "SharedCard" or "TemplatedEmails" or "SameInbox" or "DisposableEmail" or "GeneratedEmails" or "AutomationClient");
            var score = Math.Min(99m, factors.Sum(f => f.Weight));
            var formula = subs.Count == 0
                ? "No subscriptions taken yet — caught before any paid access was granted ($0 lost)."
                : $"{subs.Count} subscription(s) × list price = ${listValue:N2}, minus ${collected:N2} actually collected = ${unpaid:N2} of access given away.";

            result.Add(new ClusterAssessment(
                accts.Min(u => u.UserId), accts, score, factors, corroborated,
                corroborated && score >= alertScore && n >= minAccounts,
                topIp.Key, ipGroups.Select(g => g.Key).ToList(),
                accts.Select(u => u.DeviceFingerprint).Distinct().ToList(),
                clusterCards.Select(c => c.Key).ToList(),
                emails, accts[0].DateCreated, accts[^1].DateCreated, subs.Count, unpaid, formula));
        }
        return result.OrderByDescending(r => r.Score).ToList();
    }

    private RiskCase ToCase(SignalSet s, ClusterAssessment c)
    {
        var n = c.Accounts.Count;
        var sev = c.Score >= 85 ? RiskSeverity.Critical : c.Score >= 70 ? RiskSeverity.High : RiskSeverity.Medium;
        var top = c.Factors.Where(f => f.Signal != "SharedNetwork").OrderByDescending(f => f.Weight).Take(2)
            .Select(f => f.Signal switch
            {
                "SharedDevice" => "same device", "SharedCard" => "same card", "TemplatedEmails" => "templated emails",
                "SameInbox" => "one inbox", "DisposableEmail" => "disposable emails", "GeneratedEmails" => "generated emails",
                "AutomationClient" => "scripted client", "TrialHarvest" => "trial harvesting", _ => f.Signal
            });
        var span = Human(c.LastUtc - c.FirstUtc);
        var ids = c.Accounts.Select(u => u.UserId).ToList();

        var rc = new RiskCase
        {
            CaseKey = $"{RuleId}:cluster:{c.AnchorUserId}",
            RuleId = RuleId, RuleVersion = Version, Category = Category,
            Title = $"{n} linked accounts created from {c.PrimaryIp} in {span} · {string.Join(", ", top)}",
            Severity = sev,
            Confidence = Math.Min(0.97m, 0.55m + 0.07m * c.Factors.Count),
            Score = c.Score,
            ScoreBreakdown = c.Factors.ToList(),
            BusinessImpactUsd = Math.Round(c.UnpaidAccessUsd, 2),
            ImpactFormula = c.ImpactFormula,
            AccountId = c.AnchorUserId,
            EntityRef = $"ip:{c.PrimaryIp}",
            Expected = "Each new account belongs to a distinct person: own device, own payment method, own email identity.",
            Actual = $"{n} accounts ({string.Join(", ", ids)}) were created between {c.FirstUtc:HH:mm:ss} and {c.LastUtc:HH:mm:ss} UTC " +
                     $"from {string.Join(", ", c.Ips)} on {c.Devices.Count} device(s){(c.Emails.DominantTemplate != null ? $"; emails follow {c.Emails.DominantTemplate}" : "")}; " +
                     $"{c.SubscriptionsTaken} subscription(s) taken.",
            FirstSignalUtc = c.FirstUtc,
            TriggerSignalUtc = c.LastUtc,
        };

        foreach (var u in c.Accounts)
            rc.Evidence.Add(Ev("User", u.UserId.ToString(), $"Account {u.UserId} · {u.Email}",
                $"ip={u.RegistrationIp} · device={u.DeviceFingerprint} · {u.FirstName} {u.LastName} · {u.UserAgent}", u.DateCreated));
        foreach (var sub in s.Subscriptions.Where(x => ids.Contains(x.UserId) && x.DateActivated >= c.FirstUtc.AddMinutes(-5)))
            rc.Evidence.Add(Ev("Subscription", sub.SubscriptionId.ToString(), $"Account {sub.UserId} activated {s.PackageName(sub.PackageId)}",
                $"list ${s.PriceOf(sub.PackageId):N2} · expires {sub.DateExpiration:yyyy-MM-dd}", sub.DateActivated));
        foreach (var p in s.Payments2.Where(p => ids.Contains(p.UserId) && p.DateCreated >= c.FirstUtc.AddMinutes(-5)))
            rc.Evidence.Add(Ev("Payment", p.PaymentId.ToString(), p.PaymentTypeId == 7 ? $"Account {p.UserId} started a free trial (worth ${p.DiscountAmount:N2})" : $"Account {p.UserId} paid ${p.FinalAmount:N2}{(p.DiscountAmount > 0 ? $" (−${p.DiscountAmount:N2} discount)" : "")}",
                p.CardFingerprint != null ? $"card •••• {p.CardLast4} · {p.CardFingerprint}" : "no card (trial / complimentary)", p.DateCreated));

        foreach (var ip in c.Ips) rc.Entities.Add(new CaseEntity("ip", ip));
        foreach (var d in c.Devices) rc.Entities.Add(new CaseEntity("device", d));
        foreach (var card in c.Cards) rc.Entities.Add(new CaseEntity("card", card));
        foreach (var u in c.Accounts) { rc.Entities.Add(new CaseEntity("user", u.UserId.ToString())); rc.Entities.Add(new CaseEntity("email", u.Email)); }
        foreach (var d in c.Emails.DisposableDomains) rc.Entities.Add(new CaseEntity("domain", d));

        // the event that tipped the score may be a payment or subscription after the last signup
        rc.TriggerSignalUtc = rc.Evidence.Where(e => e.OccurredUtc != null).Select(e => e.OccurredUtc!.Value).DefaultIfEmpty(c.LastUtc).Max();

        rc.Playbook.Add($"Hold the {n} linked accounts ({string.Join(", ", ids)}) and suspend their trials/subscriptions pending review.");
        rc.Playbook.Add($"Require step-up verification (email + phone OTP) for new registrations from {c.PrimaryIp} for the next 24h.");
        if (c.Factors.Any(f => f.Signal == "SharedDevice"))
            rc.Playbook.Add($"Add device {c.Devices[0]} to the signup watchlist so any further account from it escalates this case automatically.");
        if (c.Cards.Count > 0) rc.Playbook.Add("Flag the shared card in Braintree for manual review and block it for new trials.");
        if (c.Emails.DisposableDomains.Count > 0) rc.Playbook.Add($"Reject registrations from disposable domains: {string.Join(", ", c.Emails.DisposableDomains)}.");
        if (c.SubscriptionsTaken > 0) rc.Playbook.Add("Check QBank access logs for these accounts — bulk question views suggest content is being resold or scraped.");
        return rc;
    }

    internal static string Human(TimeSpan t) =>
        t.TotalSeconds < 90 ? $"{Math.Max(1, (int)t.TotalSeconds)}s" :
        t.TotalMinutes < 90 ? $"{(int)Math.Round(t.TotalMinutes)} min" :
        t.TotalHours < 48 ? $"{t.TotalHours:N1} h" : $"{t.TotalDays:N0} days";
}
