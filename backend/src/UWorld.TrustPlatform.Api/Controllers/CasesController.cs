using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Ai;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Api.Services;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api/cases")]
public sealed class CasesController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IRiskCaseRepository _repo;
    private readonly InvestigationService _investigation;
    private readonly ICaseCopilot _copilot;
    private readonly IUsageLedger _ledger;
    private readonly IDevRequestLog _devLog;
    private readonly LiveEventHub _hub;

    public CasesController(IRiskCaseRepository repo, InvestigationService investigation, ICaseCopilot copilot,
        IUsageLedger ledger, IDevRequestLog devLog, LiveEventHub hub)
    {
        _repo = repo;
        _investigation = investigation;
        _copilot = copilot;
        _ledger = ledger;
        _devLog = devLog;
        _hub = hub;
    }

    [HttpGet]
    public async Task<IReadOnlyList<RiskCase>> GetCases(
        [FromQuery] string? ruleId, [FromQuery] string? severity, [FromQuery] string? status, [FromQuery] string? assignedTo,
        [FromQuery] string? category, [FromQuery] string? q, [FromQuery] bool? open, CancellationToken ct)
        => await _repo.GetCasesAsync(new CaseFilter(ruleId, severity, status, assignedTo, category, q, open), ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RiskCase>> GetCase(int id, CancellationToken ct)
        => await _repo.GetCaseAsync(id, ct) is { } c ? c : NotFound();

    /// <summary>Everything needed to decide the case: accounts, timeline, link graph, reputation, related cases, scoring.</summary>
    [HttpGet("{id:int}/investigation")]
    public async Task<ActionResult<CaseDossier>> Investigation(int id, CancellationToken ct)
        => await _investigation.BuildAsync(id, ct) is { } d ? d : NotFound();

    [HttpGet("{id:int}/ai-summary")]
    public async Task<ActionResult<object>> CachedSummary(int id, CancellationToken ct)
    {
        var json = await _repo.GetAiSummaryAsync(id, ct);
        return json is null ? NoContent() : Content(json, "application/json");
    }

    [HttpPost("{id:int}/ai-summary")]
    public async Task<ActionResult<CopilotSummary>> AiSummary(int id, CancellationToken ct)
    {
        var d = await _investigation.BuildAsync(id, ct);
        if (d is null) return NotFound();
        var summary = await _copilot.SummariseAsync(d.Case, d.Facts, ct);
        await _repo.SaveAiSummaryAsync(id, JsonSerializer.Serialize(summary, Json), ct);
        await Meter("ai-summary", id, d.Case.RuleId, summary.Model, summary.InputTokens, summary.OutputTokens, ct);
        await _repo.AddActivityAsync(id, "AiSummary", "copilot",
            $"{(summary.Source == "claude" ? summary.Model : "Deterministic")} summary · verdict {summary.Verdict}" +
            (summary.InputTokens > 0 ? $" · {summary.InputTokens}+{summary.OutputTokens} tokens" : "") +
            (summary.InjectionNeutralized ? $" · {summary.InjectionPatternsMatched} injection pattern(s) neutralised" : ""), ct);
        return summary;
    }

    [HttpPost("{id:int}/ask")]
    public async Task<ActionResult<CopilotAnswer>> Ask(int id, [FromBody] AskRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Question)) return BadRequest(new { error = "Ask a question about this case." });
        var d = await _investigation.BuildAsync(id, ct);
        if (d is null) return NotFound();
        var answer = await _copilot.AskAsync(d.Case, d.Facts, body.Question.Length > 500 ? body.Question[..500] : body.Question, ct);
        await Meter("ask", id, d.Case.RuleId, answer.Model, answer.InputTokens, answer.OutputTokens, ct);
        return answer;
    }

    [HttpPost("{id:int}/acknowledge")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<RiskCase>> Acknowledge(int id, CancellationToken ct)
    {
        await _repo.AcknowledgeAsync(id, HttpContext.Actor(), ct);
        await _repo.AddActivityAsync(id, "Acknowledged", HttpContext.Actor(), null, ct);
        return await Changed(id, "acknowledged", ct);
    }

    [HttpPost("{id:int}/assign")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<RiskCase>> Assign(int id, [FromBody] AssignRequest body, CancellationToken ct)
    {
        if (await _repo.GetCaseAsync(id, ct) is null) return NotFound();
        var who = string.IsNullOrWhiteSpace(body.AssignedTo) ? HttpContext.Actor() : body.AssignedTo.Trim();
        await _repo.UpdateStatusAsync(id, CaseStatus.Assigned, who, null, ct);
        await _repo.AddActivityAsync(id, "Assigned", HttpContext.Actor(), $"Owner: {who}", ct);
        return await Changed(id, $"assigned to {who}", ct);
    }

    [HttpPost("{id:int}/escalate")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<RiskCase>> Escalate(int id, [FromBody] NoteRequest body, CancellationToken ct)
    {
        if (await _repo.GetCaseAsync(id, ct) is null) return NotFound();
        await _repo.UpdateStatusAsync(id, CaseStatus.Escalated, null, null, ct);
        await _repo.AddActivityAsync(id, "Escalated", HttpContext.Actor(), body.Notes, ct);
        return await Changed(id, "escalated", ct);
    }

    [HttpPost("{id:int}/comment")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<RiskCase>> Comment(int id, [FromBody] NoteRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Notes)) return BadRequest(new { error = "Comment is empty." });
        await _repo.AddActivityAsync(id, "Comment", HttpContext.Actor(), body.Notes.Trim(), ct);
        return await _repo.GetCaseAsync(id, ct) is { } c ? c : NotFound();
    }

    /// <summary>Record that a playbook step was carried out (the platform itself never enforces — it keeps the audit trail).</summary>
    [HttpPost("{id:int}/action")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<RiskCase>> RecordAction(int id, [FromBody] NoteRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Notes)) return BadRequest(new { error = "Describe the action taken." });
        await _repo.AddActivityAsync(id, "ActionTaken", HttpContext.Actor(), body.Notes.Trim(), ct);
        return await Changed(id, "action recorded", ct);
    }

    /// <summary>Analyst verdict. Feeds rule precision; resolving requires a verdict so the metrics stay honest.</summary>
    [HttpPost("{id:int}/disposition")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<RiskCase>> Disposition(int id, [FromBody] DispositionRequest body, CancellationToken ct)
    {
        if (!Dispositions.All.Contains(body.Disposition)) return BadRequest(new { error = $"Disposition must be one of {string.Join(", ", Dispositions.All)}." });
        if (await _repo.GetCaseAsync(id, ct) is null) return NotFound();
        await _repo.SetDispositionAsync(id, body.Disposition, HttpContext.Actor(), ct);
        if (body.Resolve) await _repo.UpdateStatusAsync(id, CaseStatus.Resolved, null, DateTime.UtcNow, ct);
        await _repo.AddActivityAsync(id, body.Resolve ? "Resolved" : "Disposition", HttpContext.Actor(),
            $"{body.Disposition}{(string.IsNullOrWhiteSpace(body.Notes) ? "" : " — " + body.Notes.Trim())}", ct);
        return await Changed(id, body.Resolve ? $"resolved as {body.Disposition}" : $"marked {body.Disposition}", ct);
    }

    [HttpPost("{id:int}/resolve")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public Task<ActionResult<RiskCase>> Resolve(int id, [FromBody] DispositionRequest body, CancellationToken ct)
        => Disposition(id, body with { Resolve = true }, ct);

    [HttpPost("{id:int}/reopen")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<RiskCase>> Reopen(int id, [FromBody] NoteRequest body, CancellationToken ct)
    {
        await _repo.UpdateStatusAsync(id, CaseStatus.Assigned, HttpContext.Actor(), null, ct);
        await _repo.AddActivityAsync(id, "Reopened", HttpContext.Actor(), body.Notes, ct);
        return await Changed(id, "reopened", ct);
    }

    /// <summary>Shareable report: Markdown for tickets/email and a short chat summary. Facts only.</summary>
    [HttpGet("{id:int}/report")]
    public async Task<ActionResult<object>> Report(int id, [FromQuery] bool redact = false, [FromQuery] string? link = null, CancellationToken ct = default)
    {
        var d = await _investigation.BuildAsync(id, ct);
        if (d is null) return NotFound();
        var (md, chat) = InvestigationService.Report(d, HttpContext.Persona().DisplayName, link ?? $"/cases/{id}", redact);
        await _repo.AddActivityAsync(id, "Shared", HttpContext.Actor(), redact ? "Exported redacted report" : "Exported report", ct);
        return new { markdown = md, chat };
    }

    [HttpGet("{id:int}/dev")]
    public async Task<IReadOnlyList<DevRequestEntry>> GetCaseDev(int id, CancellationToken ct) => await _devLog.GetForCaseAsync(id, ct);

    private async Task<ActionResult<RiskCase>> Changed(int id, string what, CancellationToken ct)
    {
        var c = await _repo.GetCaseAsync(id, ct);
        if (c is null) return NotFound();
        _hub.Publish("case-status", "info", $"Case #{id} {what} by {HttpContext.Persona().DisplayName}", c.Title, caseId: id, ruleId: c.RuleId);
        return c;
    }

    private Task Meter(string op, int caseId, string ruleId, string model, int inTok, int outTok, CancellationToken ct)
        => _ledger.AddAsync(new LedgerEntry
        {
            RequestId = HttpContext.RequestId(), Kind = "AiInvestigation", Operation = $"{op}: case {caseId}", CaseId = caseId, RuleId = ruleId,
            Model = model, InputTokens = inTok, OutputTokens = outTok,
            AiCostUsd = Math.Round(inTok / 1_000_000m * 4m + outTok / 1_000_000m * 20m, 6),   // claude-opus-5-5 list price
            OccurredUtc = DateTime.UtcNow
        }, ct);
}

public sealed record AssignRequest(string? AssignedTo);
public sealed record NoteRequest(string? Notes);
public sealed record AskRequest(string? Question);
public sealed record DispositionRequest(string Disposition, string? Notes, bool Resolve);
