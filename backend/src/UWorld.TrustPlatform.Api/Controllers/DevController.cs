using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Api.DevTools;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Controllers;

/// <summary>Dev info: captured requests/responses and the code investigator. Development only.</summary>
[ApiController]
[Route("api/dev")]
public sealed class DevController : ControllerBase
{
    private readonly IDevRequestLog _log;
    private readonly CodeInvestigator _investigator;
    private readonly IWebHostEnvironment _env;

    public DevController(IDevRequestLog log, CodeInvestigator investigator, IWebHostEnvironment env)
    {
        _log = log;
        _investigator = investigator;
        _env = env;
    }

    /// <summary>Most recent captured calls. status: all | errors | 2xx | 4xx | 5xx; search matches path or request id.</summary>
    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<DevRequestEntry>>> List(
        [FromQuery] int take = 100, [FromQuery] string? status = null, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        if (!_env.IsDevelopment()) return NotFound();
        return Ok(await _log.ListAsync(Math.Clamp(take, 1, 500), status, search, ct));
    }

    [HttpGet("requests/{id:long}")]
    public async Task<ActionResult<DevRequestEntry>> Get(long id, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var e = await _log.GetAsync(id, ct);
        return e is null ? NotFound() : e;
    }

    /// <summary>Trace the request through the source code and explain the likely bug.</summary>
    [HttpPost("requests/{id:long}/investigate")]
    public async Task<ActionResult<Investigation>> Investigate(long id, CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var e = await _log.GetAsync(id, ct);
        return e is null ? NotFound() : _investigator.Investigate(e);
    }

    [HttpDelete("requests")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        if (!_env.IsDevelopment()) return NotFound();
        await _log.ClearAsync(ct);
        return NoContent();
    }
}
