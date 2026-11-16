using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardRepository _dash;
    private readonly IRiskCaseRepository _cases;

    public DashboardController(IDashboardRepository dash, IRiskCaseRepository cases)
    {
        _dash = dash;
        _cases = cases;
    }

    /// <summary>Top-N subscriptions currently being accessed (default last 15 min), with region + access count.</summary>
    [HttpGet("active-subscriptions")]
    public async Task<IReadOnlyList<ActiveSubscription>> ActiveSubscriptions(
        [FromQuery] int top = 50, [FromQuery] int minutes = 15, CancellationToken ct = default)
        => await _dash.GetActiveSubscriptionsAsync(top, minutes, ct);

    /// <summary>Fraud patterns: cases + $ at risk grouped by rule and by severity.</summary>
    [HttpGet("patterns")]
    public async Task<object> Patterns(CancellationToken ct)
    {
        var cases = await _cases.GetCasesAsync(new CaseFilter(), ct);
        return new
        {
            totalCases = cases.Count,
            totalAtRiskUsd = cases.Sum(c => c.BusinessImpactUsd),
            byRule = cases.GroupBy(c => c.RuleId)
                .Select(g => new { ruleId = g.Key, count = g.Count(), atRiskUsd = g.Sum(c => c.BusinessImpactUsd) })
                .OrderByDescending(x => x.atRiskUsd).ToList(),
            bySeverity = cases.GroupBy(c => c.Severity.ToString())
                .Select(g => new { severity = g.Key, count = g.Count() }).ToList(),
            openByStatus = cases.GroupBy(c => c.Status.ToString())
                .Select(g => new { status = g.Key, count = g.Count() }).ToList(),
        };
    }

    /// <summary>Alert feed: high-severity cases + code/package-compromise signals (a code/entity with repeat fraud).</summary>
    [HttpGet("/api/alerts")]
    public async Task<object> Alerts([FromQuery] int take = 20, CancellationToken ct = default)
    {
        var cases = await _cases.GetCasesAsync(new CaseFilter(), ct);

        var topAlerts = cases
            .Where(c => c.Severity is RiskSeverity.Critical or RiskSeverity.High && c.Status != CaseStatus.Resolved)
            .OrderByDescending(c => c.Score).Take(take)
            .Select(c => new { c.CaseId, c.RuleId, severity = c.Severity.ToString(), c.Title, c.Score, c.BusinessImpactUsd, c.CreatedUtc })
            .ToList();

        // "compromise" = an entity (discount code / package / gateway) with 2+ fraud cases
        var compromise = cases
            .Where(c => c.EntityRef != null)
            .GroupBy(c => c.EntityRef!)
            .Where(g => g.Count() >= 2)
            .Select(g => new { entity = g.Key, cases = g.Count(), atRiskUsd = g.Sum(c => c.BusinessImpactUsd) })
            .OrderByDescending(x => x.cases).ToList();

        return new { alerts = topAlerts, compromise };
    }
}
