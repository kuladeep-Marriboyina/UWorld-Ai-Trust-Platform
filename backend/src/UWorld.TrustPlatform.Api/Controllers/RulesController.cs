using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Ai;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Api.Services;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Controllers;

/// <summary>
/// The rules portal. Nobody edits a live rule directly: authors propose a version (with a reason and a dry-run
/// against live data), a different person with the Admin role approves it, and only then does it run. Every
/// proposal, approval, rejection and toggle is kept as history.
/// </summary>
[ApiController]
[Route("api/rules")]
public sealed class RulesController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Severities = { "Low", "Medium", "High", "Critical" };
    private readonly IRuleRepository _repo;
    private readonly IRiskCaseRepository _cases;
    private readonly RuleConfigLoader _loader;
    private readonly RuleConfigStore _store;
    private readonly IRuleDrafter _drafter;
    private readonly SchemaCatalog _catalog;
    private readonly DetectionRuleEngine _engine;
    private readonly ISignalRepository _signals;
    private readonly IServiceProvider _sp;
    private readonly LiveEventHub _hub;

    public RulesController(IRuleRepository repo, IRiskCaseRepository cases, RuleConfigLoader loader, RuleConfigStore store, IRuleDrafter drafter,
        SchemaCatalog catalog, DetectionRuleEngine engine, ISignalRepository signals, IServiceProvider sp, LiveEventHub hub)
    {
        _repo = repo; _cases = cases; _loader = loader; _store = store; _drafter = drafter;
        _catalog = catalog; _engine = engine; _signals = signals; _sp = sp; _hub = hub;
    }

    [HttpGet]
    public async Task<IReadOnlyList<object>> List(CancellationToken ct)
    {
        var configs = await _repo.GetConfigsAsync(ct);
        var stats = (await _repo.GetStatsAsync(ct)).ToDictionary(s => s.RuleId);
        var pending = (await _repo.GetPendingAsync(ct)).ToDictionary(v => v.RuleId);
        return configs.Select(c => View(c, stats.GetValueOrDefault(c.RuleId), pending.GetValueOrDefault(c.RuleId))).ToList();
    }

    [HttpGet("catalog")]
    public SchemaCatalog Catalog() => _catalog;

    [HttpGet("pending")]
    public async Task<IReadOnlyList<RuleVersion>> Pending(CancellationToken ct) => await _repo.GetPendingAsync(ct);

    [HttpGet("{id}")]
    public async Task<ActionResult<object>> Detail(string id, CancellationToken ct)
    {
        var config = (await _repo.GetConfigsAsync(ct)).FirstOrDefault(c => c.RuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (config is null) return NotFound();
        var stats = (await _repo.GetStatsAsync(ct)).FirstOrDefault(s => s.RuleId == config.RuleId);
        var versions = await _repo.GetVersionsAsync(config.RuleId, ct);
        var recent = (await _cases.GetCasesAsync(new CaseFilter(RuleId: config.RuleId, Take: 12), ct))
            .Select(c => new { c.CaseId, c.Title, severity = c.Severity.ToString(), status = c.Status.ToString(), c.Disposition, c.Score, c.BusinessImpactUsd, c.CreatedUtc });
        return new
        {
            rule = View(config, stats, versions.FirstOrDefault(v => v.Status == "PendingApproval")),
            versions = versions.Select(v => new
            {
                v.Id, v.VersionNo, v.Status, v.ChangeNote, v.Author, v.Reviewer, v.ReviewNote, v.CreatedUtc, v.ReviewedUtc,
                definition = v.DefinitionJson.Length > 2 ? JsonSerializer.Deserialize<RuleDefinitionDraft>(v.DefinitionJson, Json) : null
            }),
            recentCases = recent,
        };
    }

    /// <summary>AI-assisted drafting: plain English → structured rule + field mappings + clarifying questions.</summary>
    [HttpPost("draft")]
    [RequireRole(Roles.RuleAuthor, Roles.Admin)]
    public async Task<RuleInterpretation> Draft([FromBody] DraftRequest body, CancellationToken ct)
        => await _drafter.DraftAsync(body.Text ?? "", _catalog, body.Answers, ct);

    /// <summary>Dry-run a proposed rule (or a coded rule with proposed parameters) against live data. Nothing is saved.</summary>
    [HttpPost("test")]
    [RequireRole(Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<object>> Test([FromBody] TestRequest body, CancellationToken ct)
    {
        IDetectionRule rule; RuleConfigStore store = _store;
        if (body.Logic is not null)
        {
            var problems = RuleValidator.Validate(body.Logic, _catalog);
            if (problems.Count > 0) return BadRequest(new { error = "The rule isn't valid yet.", problems });
            if (string.IsNullOrWhiteSpace(body.Logic.RuleId)) body.Logic.RuleId = "draft-test";
            rule = new DeclarativeRuleEvaluator(body.Logic);
        }
        else if (body.RuleId is { } rid && _engine.Rules.FirstOrDefault(r => r.RuleId == rid) is { } coded)
        {
            if (ValidateParams(rid, body.ConfigJson) is { Count: > 0 } errs) return BadRequest(new { error = "Parameter values are out of range.", problems = errs });
            store = _store.WithOverride(rid, body.ConfigJson);
            rule = Sandbox(coded, store);
        }
        else return BadRequest(new { error = "Provide either authored logic or a coded rule id with parameters." });

        var sw = Stopwatch.StartNew();
        var signals = await _signals.LoadAllAsync(ct);
        var hits = DetectionRuleEngine.EvaluateOne(rule, signals, store);
        sw.Stop();

        var live = body.RuleId is { } id2 && _engine.Rules.FirstOrDefault(r => r.RuleId == id2) is { } current
            ? DetectionRuleEngine.EvaluateOne(current, signals, _store).Count : (int?)null;
        return new
        {
            matches = hits.Count,
            currentMatches = live,
            evaluatedMs = sw.ElapsedMilliseconds,
            rows = signals.Users.Count + signals.ApiLogs.Count + signals.Payments2.Count + signals.UserLogs.Count + signals.Subscriptions.Count,
            totalImpactUsd = hits.Sum(h => h.BusinessImpactUsd),
            bySeverity = hits.GroupBy(h => h.Severity.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            sample = hits.OrderByDescending(h => h.Score).Take(8).Select(h => new
            {
                h.Title, severity = h.Severity.ToString(), h.Score, h.BusinessImpactUsd, h.Actual, evidence = h.Evidence.Count,
                entities = h.Entities.Take(6).Select(e => $"{e.Type}:{e.Value}")
            }),
            noise = hits.Count > 25 ? "This would raise more than 25 cases right now — consider a tighter threshold or an exclusion." : null,
        };
    }

    /// <summary>Submit a new version for approval. Everything a reviewer needs must be filled in.</summary>
    [HttpPost("propose")]
    [RequireRole(Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<object>> Propose([FromBody] RuleDefinitionDraft d, CancellationToken ct)
    {
        var (problems, coded) = await PrepareDraftAsync(d, ct);
        if (problems.Count > 0) return BadRequest(new { error = "The proposal is incomplete.", problems });

        var version = await _repo.ProposeAsync(d, !coded, HttpContext.Actor(), ct);
        await _loader.ReloadAsync(ct);
        _hub.Publish("rule", "info", $"{HttpContext.Persona().DisplayName} proposed v{version} of '{d.Name}'", d.ChangeNote);
        return new { ruleId = d.RuleId, version, status = RuleStatus.PendingApproval };
    }

    /// <summary>
    /// Classic Rules Portal: create or edit a rule and put it live in one step. Same validation as a proposal, and the
    /// change is still recorded as a new version (author + change note + approver) so the history stays complete.
    /// </summary>
    [HttpPost("save")]
    [RequireRole(Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<object>> Save([FromBody] RuleDefinitionDraft d, CancellationToken ct)
    {
        var (problems, coded) = await PrepareDraftAsync(d, ct);
        if (problems.Count > 0) return BadRequest(new { error = "The rule isn't complete yet.", problems });

        var actor = HttpContext.Actor();
        var version = await _repo.ProposeAsync(d, !coded, actor, ct);
        try { await _repo.ApproveAsync(d.RuleId, version, actor, "Saved from the Rules Portal", ct); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        await _loader.ReloadAsync(ct);
        _hub.Publish("rule", "info", $"{HttpContext.Persona().DisplayName} saved v{version} of '{d.Name}' — live on the next event", d.ChangeNote);
        return new { ruleId = d.RuleId, version, status = RuleStatus.Active };
    }

    /// <summary>Validates a draft and normalises it (authored logic gets its id/name/etc. from the draft).</summary>
    private async Task<(List<string> Problems, bool Coded)> PrepareDraftAsync(RuleDefinitionDraft d, CancellationToken ct)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Trim().Length < 3) problems.Add("Give the rule a clear name.");
        if (string.IsNullOrWhiteSpace(d.Description) || d.Description.Trim().Length < 15) problems.Add("Describe the expected (normal) behaviour in at least a sentence.");
        if (string.IsNullOrWhiteSpace(d.Rationale) || d.Rationale.Trim().Length < 15) problems.Add("Explain the business reason — what loss does this rule prevent?");
        if (string.IsNullOrWhiteSpace(d.ChangeNote)) problems.Add("Add a change note for the reviewer.");
        if (!Severities.Contains(d.Severity)) problems.Add("Severity must be Low, Medium, High or Critical.");
        if (d.Playbook.Count == 0) problems.Add("List at least one response step so analysts know what to do when it fires.");

        var configs = await _repo.GetConfigsAsync(ct);
        var coded = _engine.Rules.Any(r => r.RuleId.Equals(d.RuleId, StringComparison.OrdinalIgnoreCase));
        if (coded)
        {
            d.Logic = null;
            problems.AddRange(ValidateParams(d.RuleId, d.ConfigJson));
        }
        else
        {
            if (d.Logic is null) problems.Add("An authored rule needs its detection logic.");
            else
            {
                problems.AddRange(RuleValidator.Validate(d.Logic, _catalog));
                if (string.IsNullOrWhiteSpace(d.RuleId)) d.RuleId = DeterministicRuleParser.Slug(d.Name);
                d.Logic.RuleId = d.RuleId; d.Logic.Name = d.Name; d.Logic.Category = d.Category; d.Logic.Severity = d.Severity;
                d.Logic.Description = d.Description; d.Logic.Playbook = d.Playbook; d.Logic.Author = HttpContext.Actor();
            }
            var existing = configs.FirstOrDefault(c => c.RuleId == d.RuleId);
            if (existing is not null && !existing.IsDeclarative) problems.Add($"'{d.RuleId}' is a coded rule id.");
        }
        return (problems, coded);
    }

    [HttpPost("{id}/versions/{version:int}/approve")]
    [RequireRole(Roles.Admin)]
    public async Task<ActionResult<object>> Approve(string id, int version, [FromBody] NoteRequest body, CancellationToken ct)
    {
        var v = await _repo.GetVersionAsync(id, version, ct);
        if (v is null) return NotFound();
        if (v.Author.Equals(HttpContext.Actor(), StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { error = "Four-eyes rule: you can't approve your own change. Ask another Admin to review it." });
        try { await _repo.ApproveAsync(id, version, HttpContext.Actor(), body.Notes, ct); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        await _loader.ReloadAsync(ct);
        _hub.Publish("rule", "info", $"{HttpContext.Persona().DisplayName} approved v{version} of {id} — live on the next event", body.Notes);
        return new { id, version, status = RuleStatus.Active };
    }

    [HttpPost("{id}/versions/{version:int}/reject")]
    [RequireRole(Roles.Admin)]
    public async Task<ActionResult<object>> Reject(string id, int version, [FromBody] NoteRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Notes)) return BadRequest(new { error = "Tell the author why it was rejected." });
        await _repo.RejectAsync(id, version, HttpContext.Actor(), body.Notes, ct);
        await _loader.ReloadAsync(ct);
        _hub.Publish("rule", "low", $"{HttpContext.Persona().DisplayName} rejected v{version} of {id}", body.Notes);
        return new { id, version, status = RuleStatus.Rejected };
    }

    /// <summary>Enable/disable a rule. Accepts the JSON body form, or the classic Rules Portal form (?enabled=true|false).</summary>
    [HttpPost("{id}/toggle")]
    [RequireRole(Roles.Admin)]
    public async Task<ActionResult<object>> Toggle(string id, [FromBody] ToggleRequest? body, [FromQuery] bool? enabled, CancellationToken ct)
    {
        var on = enabled ?? body?.Enabled ?? false;
        var notes = body?.Notes;
        if (enabled is not null && string.IsNullOrWhiteSpace(notes)) notes = $"{(on ? "Enabled" : "Disabled")} from the Rules Portal";
        if (!on && string.IsNullOrWhiteSpace(notes)) return BadRequest(new { error = "Disabling a detection needs a reason on record." });
        await _repo.SetEnabledAsync(id, on, HttpContext.Actor(), notes, ct);
        await _loader.ReloadAsync(ct);
        _hub.Publish("rule", on ? "info" : "medium", $"{HttpContext.Persona().DisplayName} {(on ? "enabled" : "disabled")} {id}", notes);
        return new { id, enabled = on };
    }

    // ---------------------------------------------------------------------------------------------
    // Classic Rules Portal (plain-English authoring) — interpret, answer, then activate in one step.
    // Activation still goes through the versioned store (propose + approve) so history is kept.
    // ---------------------------------------------------------------------------------------------

    [HttpPost("author")]
    [HttpPost("author/answer")]
    [RequireRole(Roles.RuleAuthor, Roles.Admin)]
    public async Task<RuleInterpretation> Author([FromBody] DraftRequest body, CancellationToken ct)
        => await _drafter.DraftAsync(body.Text ?? "", _catalog, body.Answers, ct);

    [HttpPost("author/confirm")]
    [RequireRole(Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<object>> Confirm([FromBody] DeclarativeRule rule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.Target) || rule.When.Count == 0)
            return BadRequest(new { error = "Rule needs a target and at least one condition." });
        var problems = RuleValidator.Validate(rule, _catalog);
        if (problems.Count > 0) return BadRequest(new { error = "The rule isn't valid yet.", problems });

        if (string.IsNullOrWhiteSpace(rule.Name)) rule.Name = string.IsNullOrWhiteSpace(rule.EnglishSource) ? "Authored rule" : rule.EnglishSource;
        if (string.IsNullOrWhiteSpace(rule.RuleId)) rule.RuleId = DeterministicRuleParser.Slug(rule.Name);
        if (!Severities.Contains(rule.Severity)) rule.Severity = "Medium";
        if (_engine.Rules.Any(r => r.RuleId.Equals(rule.RuleId, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { error = $"'{rule.RuleId}' is a coded rule id." });

        var actor = HttpContext.Actor();
        rule.Author = actor;
        var draft = new RuleDefinitionDraft
        {
            RuleId = rule.RuleId, Name = rule.Name, Category = rule.Category, Severity = rule.Severity,
            Description = string.IsNullOrWhiteSpace(rule.Description) ? rule.EnglishSource : rule.Description!,
            Rationale = rule.EnglishSource, Playbook = rule.Playbook, Logic = rule,
            ChangeNote = "Authored in plain English from the Rules Portal",
        };
        var version = await _repo.ProposeAsync(draft, true, actor, ct);
        await _repo.ApproveAsync(rule.RuleId, version, actor, "Activated from the Rules Portal", ct);
        await _loader.ReloadAsync(ct);
        _hub.Publish("rule", "info", $"{HttpContext.Persona().DisplayName} activated '{rule.Name}'", rule.EnglishSource);
        return new { activated = rule.RuleId, rule.Target, conditions = rule.When.Count };
    }

    private object View(RuleConfigEntry c, RuleStats? s, RuleVersion? pending)
    {
        var decided = (s?.Confirmed ?? 0) + (s?.FalsePositive ?? 0) + (s?.Benign ?? 0);
        RuleParameterCatalog.ByRule.TryGetValue(c.RuleId, out var parameters);
        DeclarativeRule? logic = null;
        if (c.IsDeclarative && c.DeclarativeJson is { Length: > 0 }) { try { logic = JsonSerializer.Deserialize<DeclarativeRule>(c.DeclarativeJson); } catch { } }
        List<string> playbook = new();
        if (c.PlaybookJson is { Length: > 0 }) { try { playbook = JsonSerializer.Deserialize<List<string>>(c.PlaybookJson) ?? new(); } catch { } }
        return new
        {
            c.RuleId, c.Name, c.Category, c.Severity, c.Description, c.Rationale, c.Enabled, c.Status, c.IsLive,
            kind = c.IsDeclarative ? "authored" : "coded",
            codeVersion = c.Version, c.VersionNo, c.Owner, c.Author, c.ApprovedBy, c.ApprovedUtc, c.DateUpdated,
            config = c.ConfigJson, parameters, logic, playbook,
            configJson = c.ConfigJson, declarativeJson = c.DeclarativeJson,   // field names the classic Rules Portal reads
            pending = pending is null ? null : new { pending.VersionNo, pending.Author, pending.ChangeNote, pending.CreatedUtc },
            stats = new
            {
                total = s?.Total ?? 0, open = s?.Open ?? 0, last24h = s?.Last24h ?? 0, lastFiredUtc = s?.LastFiredUtc,
                confirmed = s?.Confirmed ?? 0, falsePositive = s?.FalsePositive ?? 0, benign = s?.Benign ?? 0,
                precision = decided > 0 ? Math.Round((double)(s!.Confirmed) / decided, 3) : (double?)null,
                openAtRiskUsd = s?.OpenAtRiskUsd ?? 0, confirmedUsd = s?.ConfirmedUsd ?? 0,
            }
        };
    }

    private static List<string> ValidateParams(string ruleId, string? configJson)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configJson)) return errors;
        JsonElement root;
        try { root = JsonDocument.Parse(configJson).RootElement; }
        catch { return new List<string> { "Parameters are not valid JSON." }; }
        if (!RuleParameterCatalog.ByRule.TryGetValue(ruleId, out var specs)) return errors;
        foreach (var p in specs)
        {
            if (!root.TryGetProperty(p.Key, out var v)) continue;
            if (p.Type is "int" or "number")
            {
                if (!v.TryGetDouble(out var d)) { errors.Add($"{p.Label} must be a number."); continue; }
                if (p.Type == "int" && d % 1 != 0) errors.Add($"{p.Label} must be a whole number.");
                if (p.Min is double min && d < min) errors.Add($"{p.Label} must be at least {min} {p.Unit}.");
                if (p.Max is double max && d > max) errors.Add($"{p.Label} must be at most {max} {p.Unit}.");
            }
            else if (p.Type == "list" && v.ValueKind != JsonValueKind.Array) errors.Add($"{p.Label} must be a list.");
        }
        return errors;
    }

    private IDetectionRule Sandbox(IDetectionRule current, RuleConfigStore store)
    {
        var type = current.GetType();
        var takesConfig = type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(RuleConfigStore)));
        return takesConfig ? (IDetectionRule)ActivatorUtilities.CreateInstance(_sp, type, store) : current;
    }
}

public sealed record DraftRequest(string? Text, Dictionary<string, string>? Answers);
public sealed record TestRequest(DeclarativeRule? Logic, string? RuleId, string? ConfigJson);
public sealed record ToggleRequest(bool Enabled, string? Notes);
