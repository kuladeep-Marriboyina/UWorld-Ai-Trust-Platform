using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Ai;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Api.Services;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api/rules")]
public sealed class RulesController : ControllerBase
{
    private readonly IRuleRepository _repo;
    private readonly RuleConfigLoader _loader;
    private readonly IRuleInterpreter _interpreter;
    private readonly SchemaCatalog _catalog;

    public RulesController(IRuleRepository repo, RuleConfigLoader loader, IRuleInterpreter interpreter, SchemaCatalog catalog)
    {
        _repo = repo;
        _loader = loader;
        _interpreter = interpreter;
        _catalog = catalog;
    }

    /// <summary>All rules — coded + NL-authored — with enabled/severity/category/config.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<RuleConfigEntry>> Get(CancellationToken ct)
        => await _repo.GetConfigsAsync(ct);

    [HttpGet("catalog")]
    public SchemaCatalog Catalog() => _catalog;

    /// <summary>Step 1 — interpret a plain-English rule: proposed rule + field mappings + clarifying questions.</summary>
    [HttpPost("author")]
    [RequireRole("Analyst", "Lead")]
    public ActionResult<RuleInterpretation> Author([FromBody] AuthorRequest body)
        => _interpreter.Interpret(body.Text ?? "", _catalog, body.Answers);

    /// <summary>Step 2 — re-interpret with the analyst's answers to the clarifying questions.</summary>
    [HttpPost("author/answer")]
    [RequireRole("Analyst", "Lead")]
    public ActionResult<RuleInterpretation> Answer([FromBody] AuthorRequest body)
        => _interpreter.Interpret(body.Text ?? "", _catalog, body.Answers);

    /// <summary>Step 3 — persist &amp; activate the compiled rule. Engine uses it on the next pass.</summary>
    [HttpPost("author/confirm")]
    [RequireRole("Analyst", "Lead")]
    public async Task<ActionResult<object>> Confirm([FromBody] DeclarativeRule rule, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rule.RuleId) || rule.When.Count == 0)
            return BadRequest(new { error = "Rule needs an id and at least one condition." });
        rule.Author = (HttpContext.Items[RoleContextMiddleware.ItemKey] as string ?? "analyst").ToLowerInvariant();
        await _repo.UpsertDeclarativeAsync(rule, ct);
        await _loader.ReloadAsync(ct);
        return new { activated = rule.RuleId, rule.Target, conditions = rule.When.Count };
    }

    [HttpPost("{id}/toggle")]
    [RequireRole("Lead")]
    public async Task<ActionResult<object>> Toggle(string id, [FromQuery] bool enabled, CancellationToken ct)
    {
        await _repo.SetEnabledAsync(id, enabled, ct);
        await _loader.ReloadAsync(ct);
        return new { id, enabled };
    }
}

public sealed record AuthorRequest(string? Text, Dictionary<string, string>? Answers);
