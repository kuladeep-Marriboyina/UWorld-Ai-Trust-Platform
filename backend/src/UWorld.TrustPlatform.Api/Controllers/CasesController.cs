using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Ai;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api/cases")]
public sealed class CasesController : ControllerBase
{
    private readonly IRiskCaseRepository _repo;
    private readonly ICopilot _copilot;
    private readonly CostCalculator _cost;
    private readonly IUsageLedger _ledger;
    private readonly IDevRequestLog _devLog;

    public CasesController(IRiskCaseRepository repo, ICopilot copilot, CostCalculator cost, IUsageLedger ledger, IDevRequestLog devLog)
    {
        _repo = repo;
        _copilot = copilot;
        _cost = cost;
        _ledger = ledger;
        _devLog = devLog;
    }

    /// <summary>Captured request payload + response for the HTTP call that produced this case (via the ledger).</summary>
    [HttpGet("{id:int}/dev")]
    public async Task<IReadOnlyList<DevRequestEntry>> GetCaseDev(int id, CancellationToken ct)
        => await _devLog.GetForCaseAsync(id, ct);

    [HttpGet]
    public async Task<IReadOnlyList<RiskCase>> GetCases(
        [FromQuery] string? ruleId, [FromQuery] string? severity,
        [FromQuery] string? status, [FromQuery] string? assignedTo, CancellationToken ct)
        => await _repo.GetCasesAsync(new CaseFilter(ruleId, severity, status, assignedTo), ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RiskCase>> GetCase(int id, CancellationToken ct)
    {
        var c = await _repo.GetCaseAsync(id, ct);
        return c is null ? NotFound() : c;
    }

    [HttpPost("{id:int}/ai-summary")]
    public async Task<ActionResult<CopilotSummary>> AiSummary(int id, CancellationToken ct)
    {
        var c = await _repo.GetCaseAsync(id, ct);
        if (c is null) return NotFound();

        var sw = Stopwatch.StartNew();
        var summary = _copilot.Summarise(c);
        sw.Stop();

        // Meter the investigation: estimate tokens from what went in (the case) and came out (the summary).
        var input = JsonSerializer.Serialize(new { c.Title, c.Expected, c.Actual, c.ScoreBreakdown, c.Evidence });
        var output = JsonSerializer.Serialize(summary);
        var cost = _cost.Price(input, output);

        await _ledger.AddAsync(new LedgerEntry
        {
            RequestId = HttpContext.Items[RequestTracingMiddleware.ItemKey] as string ?? "req-adhoc",
            Kind = "AiInvestigation", Operation = $"ai-summary: case {id}",
            CaseId = id, RuleId = c.RuleId, Model = cost.Model,
            InputTokens = cost.InputTokens, OutputTokens = cost.OutputTokens, AiCostUsd = cost.CostUsd,
            DurationMs = (int)sw.ElapsedMilliseconds, OccurredUtc = DateTime.UtcNow
        }, ct);

        await _repo.AddActivityAsync(id, "AiSummary", "copilot",
            $"Advisory summary · {cost.InputTokens}+{cost.OutputTokens} tok · ${cost.CostUsd}", ct);
        return summary;
    }

    [HttpPost("{id:int}/assign")]
    [RequireRole("Analyst", "Lead")]
    public async Task<ActionResult<RiskCase>> Assign(int id, [FromBody] AssignRequest body, CancellationToken ct)
    {
        var c = await _repo.GetCaseAsync(id, ct);
        if (c is null) return NotFound();

        await _repo.UpdateStatusAsync(id, CaseStatus.Assigned, body.AssignedTo, null, ct);
        await _repo.AddActivityAsync(id, "Assigned", CurrentActor(), $"Assigned to {body.AssignedTo}", ct);
        return await _repo.GetCaseAsync(id, ct) ?? c;
    }

    [HttpPost("{id:int}/escalate")]
    [RequireRole("Analyst", "Lead")]
    public async Task<ActionResult<RiskCase>> Escalate(int id, [FromBody] NoteRequest body, CancellationToken ct)
    {
        var c = await _repo.GetCaseAsync(id, ct);
        if (c is null) return NotFound();

        await _repo.UpdateStatusAsync(id, CaseStatus.Escalated, null, null, ct);
        await _repo.AddActivityAsync(id, "Escalated", CurrentActor(), body.Notes, ct);
        return await _repo.GetCaseAsync(id, ct) ?? c;
    }

    [HttpPost("{id:int}/resolve")]
    [RequireRole("Lead")]
    public async Task<ActionResult<RiskCase>> Resolve(int id, [FromBody] NoteRequest body, CancellationToken ct)
    {
        var c = await _repo.GetCaseAsync(id, ct);
        if (c is null) return NotFound();

        await _repo.UpdateStatusAsync(id, CaseStatus.Resolved, null, DateTime.UtcNow, ct);
        await _repo.AddActivityAsync(id, "Resolved", CurrentActor(), body.Notes, ct);
        return await _repo.GetCaseAsync(id, ct) ?? c;
    }

    private string CurrentActor() => (HttpContext.Items[RoleContextMiddleware.ItemKey] as string ?? "analyst").ToLowerInvariant();
}

public sealed record AssignRequest(string AssignedTo);
public sealed record NoteRequest(string? Notes);
