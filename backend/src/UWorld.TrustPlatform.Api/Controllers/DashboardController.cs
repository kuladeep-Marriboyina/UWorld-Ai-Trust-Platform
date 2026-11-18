using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Ai;
using UWorld.TrustPlatform.Api.Services;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;
using UWorld.TrustPlatform.Detection.Rules;
using UWorld.TrustPlatform.Domain;
using UWorld.TrustPlatform.Ingestion;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardRepository _dash;
    private readonly IRiskCaseRepository _cases;
    private readonly IRuleRepository _rules;
    private readonly ISignalRepository _signals;
    private readonly AccountFarmRule _farm;
    private readonly RuleConfigStore _config;
    private readonly IngestionWorker _worker;
    private readonly LiveEventHub _hub;
    private readonly ICaseCopilot _copilot;

    public DashboardController(IDashboardRepository dash, IRiskCaseRepository cases, IRuleRepository rules, ISignalRepository signals,
        IEnumerable<IDetectionRule> detectionRules, RuleConfigStore config, IngestionWorker worker, LiveEventHub hub, ICaseCopilot copilot)
    {
        _dash = dash; _cases = cases; _rules = rules; _signals = signals; _config = config; _worker = worker; _hub = hub; _copilot = copilot;
        _farm = detectionRules.OfType<AccountFarmRule>().First();
    }

    private static int SevRank(RiskSeverity s) => s switch { RiskSeverity.Critical => 4, RiskSeverity.High => 3, RiskSeverity.Medium => 2, _ => 1 };

    /// <summary>Everything the dashboard shows, in one round trip.</summary>
    [HttpGet("overview")]
    public async Task<object> Overview([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, 1, 24 * 14);
        var now = DateTime.UtcNow;
        var all = await _cases.GetCasesAsync(new CaseFilter(Take: 3000), ct);
        var open = all.Where(c => c.Status != CaseStatus.Resolved).ToList();
        var last30 = all.Where(c => c.CreatedUtc >= now.AddDays(-30)).ToList();
        var traffic = await _dash.GetTrafficAsync(hours, ct);
        var hotspots = await _dash.GetHotspotsAsync(8, ct);
        var ruleStats = await _rules.GetStatsAsync(ct);
        var ruleNames = (await _rules.GetConfigsAsync(ct)).ToDictionary(r => r.RuleId, r => r);

        var unack = open.Where(c => c.AcknowledgedUtc is null).ToList();
        var critical = open.Count(c => c.Severity == RiskSeverity.Critical);
        var high = open.Count(c => c.Severity == RiskSeverity.High);
        var oldestUnack = unack.Count > 0 ? (int)(now - unack.Min(c => c.CreatedUtc)).TotalMinutes : (int?)null;
        var posture = critical > 0 ? "critical" : high > 0 ? "elevated" : open.Count > 0 ? "guarded" : "calm";
        var headline = critical > 0
            ? $"{critical} critical case{(critical > 1 ? "s" : "")} open{(unack.Any(c => c.Severity == RiskSeverity.Critical) ? " — not yet acknowledged" : "")}"
            : high > 0 ? $"{high} high-severity case{(high > 1 ? "s" : "")} need review" : open.Count > 0 ? $"{open.Count} open case{(open.Count > 1 ? "s" : "")}, none urgent" : "All clear — no open risk";

        var decided = last30.Where(c => c.Disposition != null).ToList();
        var latencies = last30.Where(c => c.DetectionLatencyMs is not null).Select(c => c.DetectionLatencyMs!.Value / 1000.0).ToList();
        var acks = last30.Where(c => c.AcknowledgedUtc is not null).Select(c => (c.AcknowledgedUtc!.Value - c.CreatedUtc).TotalMinutes).Where(m => m >= 0).ToList();

        var categories = open.GroupBy(c => c.Category ?? "Other").Select(g => new
        {
            category = g.Key, open = g.Count(), atRiskUsd = g.Sum(c => c.BusinessImpactUsd),
            critical = g.Count(c => c.Severity == RiskSeverity.Critical), high = g.Count(c => c.Severity == RiskSeverity.High)
        }).OrderByDescending(x => x.critical).ThenByDescending(x => x.atRiskUsd).ThenByDescending(x => x.open).ToList();

        var trend = Enumerable.Range(0, 14).Select(i => now.Date.AddDays(-13 + i)).Select(day => new
        {
            day,
            total = all.Count(c => c.CreatedUtc.Date == day),
            confirmed = all.Count(c => c.CreatedUtc.Date == day && c.Disposition == Dispositions.ConfirmedFraud),
            falsePositive = all.Count(c => c.CreatedUtc.Date == day && c.Disposition is Dispositions.FalsePositive or Dispositions.Benign),
            impactUsd = all.Where(c => c.CreatedUtc.Date == day).Sum(c => c.BusinessImpactUsd),
            byCategory = all.Where(c => c.CreatedUtc.Date == day).GroupBy(c => c.Category ?? "Other").ToDictionary(g => g.Key, g => g.Count()),
        }).ToList();

        var queue = open
            .OrderByDescending(c => SevRank(c.Severity) * 1000 + (c.AcknowledgedUtc is null ? 300 : 0) + (double)Math.Min(c.BusinessImpactUsd, 5000m) / 50 + Math.Min(240, (now - c.CreatedUtc).TotalMinutes))
            .Take(7)
            .Select(c => new
            {
                c.CaseId, c.Title, severity = c.Severity.ToString(), c.Score, c.Category, c.RuleId, c.BusinessImpactUsd, c.CreatedUtc, c.UpdatedUtc,
                acknowledged = c.AcknowledgedUtc != null, c.AssignedTo, status = c.Status.ToString(), c.DetectionLatencyMs, c.EvidenceCount
            }).ToList();

        var rules = ruleStats.Select(s =>
        {
            var d = s.Confirmed + s.FalsePositive + s.Benign;
            ruleNames.TryGetValue(s.RuleId, out var cfg);
            return new
            {
                s.RuleId, name = cfg?.Name ?? s.RuleId, live = cfg?.IsLive ?? false, s.Total, s.Open, s.Last24h, s.Confirmed, s.FalsePositive,
                precision = d > 0 ? Math.Round((double)s.Confirmed / d, 3) : (double?)null, s.ConfirmedUsd, s.OpenAtRiskUsd, s.LastFiredUtc
            };
        }).OrderByDescending(r => r.Open).ThenByDescending(r => r.Total).ToList();

        var emerging = await EmergingAsync(ct);
        var last = _worker.LastPass;

        return new
        {
            generatedUtc = now,
            windowHours = hours,
            posture = new
            {
                level = posture, headline, openTotal = open.Count, critical, high,
                medium = open.Count(c => c.Severity == RiskSeverity.Medium), low = open.Count(c => c.Severity == RiskSeverity.Low),
                unacknowledged = unack.Count, oldestUnackMinutes = oldestUnack, unassigned = open.Count(c => c.AssignedTo is null)
            },
            kpis = new
            {
                atRiskOpenUsd = open.Sum(c => c.BusinessImpactUsd),
                protectedUsd30d = decided.Where(c => c.Disposition == Dispositions.ConfirmedFraud).Sum(c => c.BusinessImpactUsd),
                medianDetectSeconds = latencies.Count > 0 ? Median(latencies) : (double?)null,
                medianAckMinutes = acks.Count > 0 ? Median(acks) : (double?)null,
                precision30d = decided.Count > 0 ? Math.Round((double)decided.Count(c => c.Disposition == Dispositions.ConfirmedFraud) / decided.Count, 3) : (double?)null,
                decided30d = decided.Count,
                casesInWindow = all.Count(c => c.CreatedUtc >= now.AddHours(-hours)),
                accountsInWindow = traffic.Signups,
            },
            funnel = new
            {
                signals = traffic.ApiCalls + traffic.Payments + traffic.AccessEvents + traffic.Signups,
                rowsEvaluated = last?.RowsEvaluated,
                flaggedEntities = hotspots.Count,
                cases = all.Count(c => c.CreatedUtc >= now.AddHours(-hours)),
                confirmed = all.Count(c => c.CreatedUtc >= now.AddHours(-hours) && c.Disposition == Dispositions.ConfirmedFraud),
            },
            traffic,
            categories,
            trend,
            queue,
            hotspots,
            rules,
            emerging,
            engine = new { lastPassUtc = last?.CompletedUtc, lastPassMs = last?.DurationMs, lastTrigger = last?.Trigger, viewers = _hub.Connected, rules = rules.Count(r => r.live) },
        };
    }

    /// <summary>Linked-account clusters and login bursts that are building but haven't crossed the alert line yet.</summary>
    [HttpGet("emerging")]
    public async Task<IReadOnlyList<object>> EmergingAsync(CancellationToken ct)
    {
        var signals = await _signals.LoadAllAsync(ct);
        var since = DateTime.UtcNow.AddHours(-24);
        var list = new List<object>();
        foreach (var c in _farm.Assess(signals).Where(c => !c.WouldAlert && c.LastUtc >= since && c.Score >= 15).Take(6))
            list.Add(new
            {
                kind = "account-cluster", key = $"ip:{c.PrimaryIp}", title = $"{c.Accounts.Count} linked accounts from {c.PrimaryIp}",
                score = c.Score, threshold = _config.Num("account-farm", "alertScore", 55), c.Corroborated, lastSeenUtc = c.LastUtc,
                why = c.Corroborated
                    ? $"Corroborated but below the alert score ({c.Accounts.Count} of {_config.Int("account-farm", "minAccounts", 3)} accounts needed)."
                    : "Shared network only — no identity signal corroborates it (likely campus/office). Watching, not alerting.",
                factors = c.Factors.Select(f => f.Signal)
            });

        var window = TimeSpan.FromMinutes(_config.Num("credential-stuffing", "windowMinutes", 15));
        var minFail = _config.Int("credential-stuffing", "minFailures", 8);
        foreach (var g in signals.ApiLogs.Where(l => l.Outcome == "Failed" && l.Endpoint.Contains("/auth/login") && l.DateCreated >= DateTime.UtcNow - window)
                     .GroupBy(l => l.IPAddress).Where(g => g.Count() >= 3 && g.Count() < minFail))
            list.Add(new
            {
                kind = "login-burst", key = $"ip:{g.Key}", title = $"{g.Count()} failed logins from {g.Key}",
                score = Math.Round(100m * g.Count() / minFail), threshold = 100, corroborated = g.Select(x => x.Email).Distinct().Count() > 1, lastSeenUtc = g.Max(x => x.DateCreated),
                why = $"{g.Select(x => x.Email).Distinct().Count()} account(s) targeted; alerts at {minFail} failures across {_config.Int("credential-stuffing", "minDistinctAccounts", 5)} accounts.",
                factors = new[] { "FailedLoginBurst" }
            });
        return list;
    }

    /// <summary>AI shift briefing grounded in the overview numbers.</summary>
    [HttpPost("briefing")]
    public async Task<Briefing> Briefing(CancellationToken ct)
    {
        var all = await _cases.GetCasesAsync(new CaseFilter(OpenOnly: true, Take: 500), ct);
        var traffic = await _dash.GetTrafficAsync(24, ct);
        var hotspots = await _dash.GetHotspotsAsync(5, ct);
        var facts = new List<string>
        {
            $"{all.Count} open cases: {all.Count(c => c.Severity == RiskSeverity.Critical)} critical, {all.Count(c => c.Severity == RiskSeverity.High)} high; {all.Count(c => c.AcknowledgedUtc is null)} not yet acknowledged.",
            $"${all.Sum(c => c.BusinessImpactUsd):N0} of revenue is at risk across open cases.",
            $"Last 24h traffic: {traffic.Signups} new accounts, {traffic.ApiCalls} API calls, {traffic.FailedLogins} failed logins, {traffic.Payments} payments.",
        };
        facts.AddRange(all.GroupBy(c => c.Category ?? "Other").OrderByDescending(g => g.Count()).Select(g => $"{g.Key}: {g.Count()} open case(s), ${g.Sum(c => c.BusinessImpactUsd):N0} at risk."));
        facts.AddRange(all.Where(c => c.Severity is RiskSeverity.Critical or RiskSeverity.High).Take(5)
            .Select(c => InvestigationService.Mask($"Case #{c.CaseId} ({c.Severity}, {(c.AcknowledgedUtc is null ? "unacknowledged" : "acknowledged")}, opened {(DateTime.UtcNow - c.CreatedUtc).TotalMinutes:N0} min ago): {c.Title}")));
        facts.AddRange(hotspots.Select(h => InvestigationService.Mask($"Hotspot {h.Type} {h.Value}: {h.Cases} open case(s), {h.Accounts} accounts, ${h.AtRiskUsd:N0}.")));
        return await _copilot.BriefAsync(facts, ct);
    }

    [HttpGet("active-subscriptions")]
    public async Task<IReadOnlyList<ActiveSubscription>> ActiveSubscriptions([FromQuery] int top = 20, [FromQuery] int minutes = 60, CancellationToken ct = default)
        => await _dash.GetActiveSubscriptionsAsync(top, minutes, ct);

    private static double Median(List<double> xs)
    {
        var s = xs.OrderBy(x => x).ToList();
        return Math.Round(s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2, 2);
    }
}
