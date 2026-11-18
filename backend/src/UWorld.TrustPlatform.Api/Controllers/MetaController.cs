using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Ai;
using UWorld.TrustPlatform.Api.Security;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class MetaController : ControllerBase
{
    private readonly IAiProvider _ai;
    public MetaController(IAiProvider ai) => _ai = ai;

    /// <summary>The acting user, their role and what they're allowed to do (drives what the UI enables).</summary>
    [HttpGet("me")]
    public object Me()
    {
        var p = HttpContext.Persona();
        return new { p.Username, p.DisplayName, p.Role, p.Title, p.Initials, permissions = PlatformDirectory.Permissions(p.Role) };
    }

    [HttpGet("people")]
    public IEnumerable<object> People() => PlatformDirectory.People.Select(p => new { p.Username, p.DisplayName, p.Role, p.Title, p.Initials, permissions = PlatformDirectory.Permissions(p.Role) });

    [HttpGet("ai/status")]
    public AiStatus AiStatus() => _ai.Status;

    /// <summary>Makes one tiny real call to the model so you can confirm the key works before a demo.</summary>
    [HttpPost("ai/test")]
    public async Task<object> TestAi(CancellationToken ct)
    {
        if (!_ai.Status.Live) return new { ok = false, provider = _ai.Status.Provider, detail = _ai.Status.Detail };
        var call = await _ai.JsonAsync(
            "You are a connectivity check. Reply with the requested JSON only.",
            "Return ok=true and a five-word greeting for a fraud analyst.",
            Schema.Obj(new() { ["ok"] = Schema.Bool(), ["greeting"] = Schema.Str() }), "low", 200, ct);
        return call is null
            ? new { ok = false, provider = "claude", detail = _ai.Status.Detail }
            : (object)new { ok = true, provider = "claude", model = call.Model, latencyMs = call.DurationMs, call.InputTokens, call.OutputTokens,
                greeting = call.Json.TryGetProperty("greeting", out var g) ? g.GetString() : null };
    }
}
