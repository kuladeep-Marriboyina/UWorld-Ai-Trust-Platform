using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Ai;

namespace UWorld.TrustPlatform.Api.Controllers;

/// <summary>
/// Red-team endpoint: paste an attempted "make the AI hack us" instruction and see exactly what the
/// guard does with it. The text is treated as untrusted data - injection patterns are detected and
/// neutralized before anything would reach a model. And even un-neutralized, the copilot has no tool
/// that could change access, accounts or payments.
/// </summary>
[ApiController]
[Route("api/ai")]
public sealed class AiSafetyController : ControllerBase
{
    private readonly IPromptInjectionGuard _guard;

    public AiSafetyController(IPromptInjectionGuard guard) => _guard = guard;

    [HttpPost("inspect")]
    public ActionResult<InjectionInspection> Inspect([FromBody] InspectRequest body)
        => _guard.Inspect(body.Text);
}

public sealed record InspectRequest(string Text);
