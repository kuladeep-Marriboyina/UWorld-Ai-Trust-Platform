using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Services;

public sealed record TimelineItem(DateTime Utc, string Kind, string Title, string? Detail, string? Ip, bool Highlight, string? Ref);
public sealed record GraphNode(string Id, string Type, string Label, string? Sub, bool Flagged);
public sealed record GraphEdge(string From, string To, string Label);
public sealed record CaseStats(int Accounts, int Ips, int Devices, int Cards, int Subscriptions, int ApiCalls, int FailedLogins, double? SpanSeconds);

public sealed record CaseDossier(
    RiskCase Case,
    IReadOnlyList<AccountProfile> Accounts,
    IReadOnlyList<TimelineItem> Timeline,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<EntityReputation> Reputation,
    IReadOnlyList<RelatedCase> Related,
    CaseStats Stats,
    IReadOnlyList<string> Facts,
    object Scoring);

/// <summary>
/// Assembles everything needed to decide a case on one screen, straight from source tables — no interpretation
/// added. The same dossier feeds the UI, the AI fact sheet (masked) and the shareable report.
/// </summary>
public sealed partial class InvestigationService
{
    private readonly IRiskCaseRepository _cases;
    private readonly IIdentityRepository _identity;
    private readonly Detection.RuleConfigStore _config;

    public InvestigationService(IRiskCaseRepository cases, IIdentityRepository identity, Detection.RuleConfigStore config)
    {
        _cases = cases;
        _identity = identity;
        _config = config;
    }

    public async Task<CaseDossier?> BuildAsync(int caseId, CancellationToken ct)
    {
        var c = await _cases.GetCaseAsync(caseId, ct);
        if (c is null) return null;

        var userIds = c.Entities.Where(e => e.Type == "user").Select(e => int.TryParse(e.Value, out var v) ? v : 0).Where(v => v > 0).ToList();
        if (c.AccountId is int a && !userIds.Contains(a)) userIds.Insert(0, a);
        var accounts = await _identity.GetProfilesAsync(userIds, ct);
        var ips = c.Entities.Where(e => e.Type == "ip").Select(e => e.Value).Distinct().ToList();
        var activity = await _identity.GetActivityAsync(accounts.Select(x => x.UserId).ToList(), ips, 200, ct);
        var related = await _cases.GetRelatedAsync(caseId, ct);

        var reputation = new List<EntityReputation>();
        foreach (var e in c.Entities.Where(e => e.Type is "ip" or "device" or "card" or "domain").DistinctBy(e => (e.Type, e.Value)).Take(8))
            reputation.Add(await _identity.GetReputationAsync(e.Type, e.Value, caseId, ct));

        var timeline = BuildTimeline(c, accounts, activity);
        var (nodes, edges) = BuildGraph(c, accounts, activity);
        var stats = new CaseStats(accounts.Count, ips.Count, c.Entities.Count(e => e.Type == "device"), c.Entities.Count(e => e.Type == "card"),
            accounts.Sum(x => x.Subscriptions.Count), activity.Count, activity.Count(x => x.Outcome == "Failed"),
            c.FirstSignalUtc is DateTime f && c.TriggerSignalUtc is DateTime t ? (t - f).TotalSeconds : null);

        var alertScore = c.RuleId == "account-farm" ? _config.Num("account-farm", "alertScore", 55) : (double?)null;
        var scoring = new
        {
            total = c.Score,
            threshold = alertScore,
            factors = c.ScoreBreakdown.OrderByDescending(s => s.Weight).Select(s => new { s.Signal, s.Weight, s.Rationale, label = FactorLabel(s.Signal) }),
            gate = c.RuleId == "account-farm"
                ? "Shared network alone never raises a case. At least one identity signal (device, card, email pattern, disposable domain, automation) must corroborate it."
                : null,
            severityBands = new[] { new { label = "Medium", from = 55 }, new { label = "High", from = 70 }, new { label = "Critical", from = 85 } },
        };

        var facts = BuildFacts(c, accounts, reputation, related, stats);
        return new CaseDossier(c, accounts, timeline, nodes, edges, reputation, related, stats, facts, scoring);
    }

    private static string FactorLabel(string signal) => signal switch
    {
        "SharedNetwork" => "Shared registration IP", "SharedDevice" => "Same device", "SharedCard" => "Same payment card",
        "TemplatedEmails" => "Templated emails", "SameInbox" => "One inbox, many aliases", "DisposableEmail" => "Disposable email",
        "GeneratedEmails" => "Machine-generated emails", "AutomationClient" => "Scripted client", "ScriptedCadence" => "Inhuman signup speed",
        "TrialHarvest" => "Trial / discount harvesting", "FailedLoginBurst" => "Failed-login burst", "AccountSpread" => "Many accounts targeted",
        "SuccessfulTakeover" => "Successful takeover", "AuthoredRule" => "Authored rule matched", "ThresholdExceeded" => "Above threshold",
        _ => Regex.Replace(signal, "(?<=[a-z])(?=[A-Z])", " ")
    };

    private static List<TimelineItem> BuildTimeline(RiskCase c, IReadOnlyList<AccountProfile> accounts, IReadOnlyList<ApiLogRecord> activity)
    {
        var items = new List<TimelineItem>();
        var evidenceLogIds = c.Evidence.Where(e => e.SourceType == "ApiLog").Select(e => e.SourceId).ToHashSet();
        var accountIds = accounts.Select(x => x.UserId).ToHashSet();

        foreach (var acct in accounts)
        {
            items.Add(new TimelineItem(acct.Created, "signup", $"Account {acct.UserId} created · {acct.Email}",
                $"{acct.FirstName} {acct.LastName} · device {acct.DeviceFingerprint} · {acct.UserAgent}", acct.RegistrationIp, true, $"user:{acct.UserId}"));
            foreach (var s in acct.Subscriptions)
                items.Add(new TimelineItem(s.Activated, "subscription", $"Account {acct.UserId} activated {s.Package}", $"list ${s.ListPrice:N2} · expires {s.Expires:yyyy-MM-dd}", null, false, $"sub:{s.SubscriptionId}"));
            foreach (var p in acct.Payments)
                items.Add(new TimelineItem(p.Created, "payment", $"Account {acct.UserId}: {p.Kind} ${p.Amount:N2}{(p.Discount > 0 ? $" (−${p.Discount:N2})" : "")}",
                    p.CardLast4 != null ? $"card •••• {p.CardLast4}" : "no card", null, false, $"payment:{p.PaymentId}"));
        }
        foreach (var l in activity)
        {
            if (l.Endpoint.Contains("/auth/register") && l.UserId is int u && accountIds.Contains(u)) continue;   // already shown as signup
            if (l.Endpoint.Contains("/orders/subscribe") && l.UserId is int u2 && accountIds.Contains(u2)) continue;
            var failed = l.Outcome == "Failed";
            items.Add(new TimelineItem(l.DateCreated, failed ? "login-failed" : l.Endpoint.Contains("login") ? "login" : "api",
                $"{l.Method} {l.Endpoint} → {l.StatusCode}{(l.Email != null ? $" · {l.Email}" : "")}",
                $"{l.Outcome}{(l.UserAgent != null ? " · " + l.UserAgent : "")}", l.IPAddress, evidenceLogIds.Contains(l.Id.ToString()), $"log:{l.Id}"));
        }
        foreach (var e in c.Evidence.Where(e => e.OccurredUtc != null && e.SourceType is not ("User" or "Subscription" or "Payment" or "ApiLog")))
            items.Add(new TimelineItem(e.OccurredUtc!.Value, "evidence", e.Label, e.Detail, null, true, $"{e.SourceType}:{e.SourceId}"));
        foreach (var act in c.Activity)
            items.Add(new TimelineItem(act.OccurredUtc, act.Action == "Created" ? "detected" : "activity",
                act.Action == "Created" ? $"Case raised (score {c.Score:N0})" : $"{act.Action} by {act.Actor}", act.Notes, null, act.Action is "Created" or "Updated" or "Reopened", null));
        return items.OrderBy(i => i.Utc).ToList();
    }

    private static (List<GraphNode>, List<GraphEdge>) BuildGraph(RiskCase c, IReadOnlyList<AccountProfile> accounts, IReadOnlyList<ApiLogRecord> activity)
    {
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new HashSet<GraphEdge>();
        var flaggedIps = c.Entities.Where(e => e.Type == "ip").Select(e => e.Value).ToHashSet();
        var flaggedDevices = c.Entities.Where(e => e.Type == "device").Select(e => e.Value).ToHashSet();
        var flaggedCards = c.Entities.Where(e => e.Type == "card").Select(e => e.Value).ToHashSet();

        if (accounts.Count > 0)
        {
            var report = IdentityAnalysis.Analyze(accounts.Select(x => x.Email).ToList());
            var templateStem = accounts.GroupBy(x => IdentityAnalysis.Stem(x.Email))
                .Where(g => g.Key.Length >= 4 && g.Count() >= 2).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            foreach (var a in accounts)
            {
                var id = $"user:{a.UserId}";
                nodes[id] = new GraphNode(id, "account", $"#{a.UserId}", a.Email, true);
                Link($"ip:{a.RegistrationIp}", "ip", a.RegistrationIp, null, flaggedIps.Contains(a.RegistrationIp), id, "registered from");
                Link($"device:{a.DeviceFingerprint}", "device", a.DeviceFingerprint, null, flaggedDevices.Contains(a.DeviceFingerprint), id, "created on");
                foreach (var card in a.Payments.Where(p => p.CardFingerprint != null).Select(p => (p.CardFingerprint!, p.CardLast4)).Distinct())
                    Link($"card:{card.Item1}", "card", $"•••• {card.CardLast4}", card.Item1, flaggedCards.Contains(card.Item1), id, "paid with");
                if (templateStem != null && IdentityAnalysis.Stem(a.Email) == templateStem)
                    Link("pattern:" + templateStem, "pattern", report.DominantTemplate!, "email template", true, id, "matches");
                else if (IdentityAnalysis.IsDisposable(a.Email))
                    Link("domain:" + IdentityAnalysis.Domain(a.Email), "domain", IdentityAnalysis.Domain(a.Email), "disposable", true, id, "uses");
            }
            // drop entity nodes that touch only one account and aren't flagged — they don't explain any link
            var degree = edges.GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.Count());
            foreach (var n in nodes.Values.Where(n => n.Type != "account" && !n.Flagged && degree.GetValueOrDefault(n.Id) < 2).ToList())
            {
                nodes.Remove(n.Id);
                edges.RemoveWhere(e => e.To == n.Id);
            }
        }
        else
        {
            foreach (var ip in flaggedIps)
            {
                nodes[$"ip:{ip}"] = new GraphNode($"ip:{ip}", "ip", ip, "source", true);
                foreach (var g in activity.Where(l => l.IPAddress == ip && l.Email != null).GroupBy(l => l.Email!).Take(18))
                {
                    var ok = g.Any(l => l.Outcome == "Success");
                    var nid = $"email:{g.Key}";
                    nodes[nid] = new GraphNode(nid, ok ? "account" : "email", g.Key, ok ? "logged in" : $"{g.Count()} failed", ok);
                    edges.Add(new GraphEdge($"ip:{ip}", nid, ok ? "logged in" : "failed login"));
                }
            }
        }
        return (nodes.Values.ToList(), edges.ToList());

        void Link(string id, string type, string label, string? sub, bool flagged, string from, string rel)
        {
            if (!nodes.ContainsKey(id)) nodes[id] = new GraphNode(id, type, label, sub, flagged);
            edges.Add(new GraphEdge(from, id, rel));
        }
    }

    [GeneratedRegex(@"\b(\d{1,3}\.\d{1,3}\.\d{1,3})\.\d{1,3}\b")] private static partial Regex IpLastOctet();
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")] private static partial Regex Email();

    /// <summary>Masks identifiers before anything leaves for the model: emails keep their shape, IPs lose the last octet.</summary>
    public static string Mask(string text)
    {
        text = Email().Replace(text, m => IdentityAnalysis.Mask(m.Value));
        return IpLastOctet().Replace(text, "$1.x");
    }

    private static List<string> BuildFacts(RiskCase c, IReadOnlyList<AccountProfile> accounts, IReadOnlyList<EntityReputation> rep, IReadOnlyList<RelatedCase> related, CaseStats stats)
    {
        var f = new List<string>
        {
            $"Case raised by rule '{c.RuleId}' ({c.Category}) at {c.CreatedUtc:yyyy-MM-dd HH:mm:ss} UTC; severity {c.Severity}, score {c.Score:N0}/100, confidence {c.Confidence:P0}; status {c.Status}{(c.Disposition != null ? $", disposition {c.Disposition}" : "")}.",
            c.DetectionLatencyMs is int ms ? $"Detected {Durations.Short(ms)} after the event that pushed it over the threshold." : "Found on a backfill sweep, not in real time.",
            $"Policy expectation: {c.Expected}",
            $"Observed: {c.Actual}",
            $"Impact: {c.ImpactFormula ?? $"${c.BusinessImpactUsd:N2}"}",
        };
        if (stats.SpanSeconds is double span) f.Add($"Attack window: {c.FirstSignalUtc:HH:mm:ss}–{c.TriggerSignalUtc:HH:mm:ss} UTC ({span:N0} seconds).");
        f.AddRange(c.ScoreBreakdown.OrderByDescending(s => s.Weight).Select(s => $"Score factor {s.Signal} (+{s.Weight:N0}): {s.Rationale}"));
        foreach (var a in accounts.Take(12))
            f.Add($"Account {a.UserId}: email {a.Email}, name {a.FirstName} {a.LastName[..Math.Min(1, a.LastName.Length)]}., created {a.Created:HH:mm:ss} UTC from {a.RegistrationIp} on device {a.DeviceFingerprint}, client '{a.UserAgent}', " +
                  $"{a.Subscriptions.Count} subscription(s){(a.Subscriptions.Count > 0 ? $" ({string.Join(", ", a.Subscriptions.Select(s => s.Package))})" : "")}, paid ${a.Payments.Sum(p => p.Amount):N2}, {a.ApiCalls} API calls, {a.FailedLogins} failed logins.");
        if (accounts.Count > 12) f.Add($"…and {accounts.Count - 12} more linked accounts.");
        f.AddRange(rep.Select(r => $"{r.Type} {r.Value}: {r.Accounts} account(s) linked, {r.ApiCalls} API calls, {r.FailedLogins} failed logins, countries [{string.Join(", ", r.Countries)}]. {r.Verdict}"));
        if (related.Count > 0) f.Add($"{related.Select(r => r.CaseId).Distinct().Count()} other case(s) share an identifier with this one: " + string.Join("; ", related.Take(5).Select(r => $"#{r.CaseId} {r.Title} (shares {r.SharedType})")));
        f.AddRange(c.Activity.TakeLast(6).Select(a => $"Activity {a.OccurredUtc:HH:mm}: {a.Action} by {a.Actor}{(a.Notes != null ? " — " + a.Notes : "")}"));
        return f.Select(Mask).ToList();
    }

    /// <summary>A factual, shareable case report (Markdown) and a short chat summary. Nothing in it is generated — only recorded facts.</summary>
    public static (string Markdown, string Chat) Report(CaseDossier d, string generatedBy, string link, bool redact)
    {
        string R(string? s) => s is null ? "" : redact ? Mask(s) : s;
        var c = d.Case;
        var sb = new StringBuilder();
        sb.AppendLine($"# Case #{c.CaseId} — {R(c.Title)}");
        sb.AppendLine();
        sb.AppendLine($"**{c.Severity}** · score {c.Score:N0}/100 · confidence {c.Confidence:P0} · status **{c.Status}**{(c.Disposition != null ? $" · disposition **{c.Disposition}**" : "")}  ");
        sb.AppendLine($"Rule `{c.RuleId}` ({c.RuleVersion}) · category {c.Category} · raised {c.CreatedUtc:yyyy-MM-dd HH:mm} UTC" +
                      (c.DetectionLatencyMs is int ms ? $" · detected {Durations.Short(ms)} after the triggering event" : "") + $" · assigned to {c.AssignedTo ?? "nobody"}  ");
        sb.AppendLine($"Link: {link}");
        sb.AppendLine();
        sb.AppendLine("## What happened");
        sb.AppendLine(R(c.Actual));
        sb.AppendLine();
        sb.AppendLine($"**Policy expects:** {c.Expected}");
        sb.AppendLine();
        sb.AppendLine("## Why it was flagged");
        foreach (var s in c.ScoreBreakdown.OrderByDescending(s => s.Weight)) sb.AppendLine($"- **+{s.Weight:N0} {FactorLabel(s.Signal)}** — {R(s.Rationale)}");
        sb.AppendLine();
        sb.AppendLine("## Business impact");
        sb.AppendLine($"${c.BusinessImpactUsd.ToString("N2", CultureInfo.InvariantCulture)} — {R(c.ImpactFormula)}");
        if (d.Accounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Linked accounts ({d.Accounts.Count})");
            sb.AppendLine("| Account | Email | Created (UTC) | Registration IP | Device | Subscriptions | Paid |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var a in d.Accounts)
                sb.AppendLine($"| {a.UserId} | {R(a.Email)} | {a.Created:MM-dd HH:mm:ss} | {R(a.RegistrationIp)} | {a.DeviceFingerprint} | {a.Subscriptions.Count} | ${a.Payments.Sum(p => p.Amount):N2} |");
        }
        if (d.Reputation.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Shared identifiers");
            foreach (var r in d.Reputation) sb.AppendLine($"- `{r.Type}` {R(r.Value)} — {r.Accounts} accounts, {r.ApiCalls} API calls, {r.FailedLogins} failed logins. {r.Verdict}");
        }
        sb.AppendLine();
        sb.AppendLine("## Recommended actions");
        foreach (var p in c.Playbook) sb.AppendLine($"- [ ] {R(p)}");
        sb.AppendLine();
        sb.AppendLine("## Case log");
        foreach (var a in c.Activity) sb.AppendLine($"- {a.OccurredUtc:yyyy-MM-dd HH:mm} — **{a.Action}** by {a.Actor}{(a.Notes != null ? ": " + R(a.Notes) : "")}");
        sb.AppendLine();
        sb.AppendLine($"_Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC by {generatedBy} from recorded evidence only.{(redact ? " Customer identifiers masked." : " Internal — contains customer identifiers.")}_");

        var chat = $"🚨 *Case #{c.CaseId}* · {c.Severity} · score {c.Score:N0} — {R(c.Title)}\n" +
                   $"• {R(c.Actual)}\n" +
                   $"• Impact: ${c.BusinessImpactUsd:N2} — {R(c.ImpactFormula)}\n" +
                   $"• Top signals: {string.Join(", ", c.ScoreBreakdown.OrderByDescending(s => s.Weight).Take(3).Select(s => FactorLabel(s.Signal)))}\n" +
                   $"• Status: {c.Status}{(c.AssignedTo != null ? $" (owner {c.AssignedTo})" : "")}\n{link}";
        return (sb.ToString(), chat);
    }
}
