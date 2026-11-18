using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Api.Services;
using UWorld.TrustPlatform.Ingestion;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    private readonly DemoSimulator _simulator;
    private readonly IngestionWorker _worker;
    private readonly INewRelicClient _telemetry;
    private readonly DemoResetService _reset;
    private readonly RuleConfigLoader _loader;
    private readonly LiveEventHub _hub;

    public DemoController(DemoSimulator simulator, IngestionWorker worker, INewRelicClient telemetry, DemoResetService reset, RuleConfigLoader loader, LiveEventHub hub)
    {
        _simulator = simulator;
        _worker = worker;
        _telemetry = telemetry;
        _reset = reset;
        _loader = loader;
        _hub = hub;
    }

    /// <summary>Data-level attack families (payment, discount, region, access) and their bypass variants.</summary>
    [HttpGet("scenarios")]
    public IReadOnlyList<ScenarioGroup> Scenarios() => DemoSimulator.Catalog;

    [HttpPost("simulate/{scenario}")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<ActionResult<SimulationResult>> SimulateScenario(string scenario, CancellationToken ct)
    {
        try
        {
            var result = await _simulator.SimulateScenarioAsync(scenario, HttpContext.RequestId(), ct);
            _hub.Publish("simulation", "info", $"Simulation injected: {scenario}", $"{result.Injected.Count} rows written · {result.CasesCreated} case(s) raised");
            return result;
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Run one detection pass on demand.</summary>
    [HttpPost("run")]
    [RequireRole(Roles.Analyst, Roles.RuleAuthor, Roles.Admin)]
    public async Task<object> Run(CancellationToken ct)
    {
        var pass = await _worker.RunPassAsync("manual run", ct, HttpContext.RequestId());
        return new { casesCreated = pass.Created, casesUpdated = pass.Updated, pass.DurationMs, pass.RowsEvaluated };
    }

    /// <summary>Restore the seeded baseline (history, campus cohort, one historical farm) and re-run detection.</summary>
    [HttpPost("reset")]
    [RequireRole(Roles.Admin)]
    public async Task<ActionResult<object>> Reset(CancellationToken ct)
    {
        try
        {
            var scripts = await _reset.ResetAsync(ct);
            await _loader.ReloadAsync(ct);
            var pass = await _worker.RunPassAsync("demo reset", ct, HttpContext.RequestId());
            _hub.Publish("system", "info", "Demo data reset to baseline", $"{pass.Created} case(s) raised on backfill");
            return new { scripts, casesRaised = pass.Created };
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("telemetry")]
    public IReadOnlyList<TelemetryEvent> Telemetry([FromQuery] int take = 25) => _telemetry.Recent(take);
}
