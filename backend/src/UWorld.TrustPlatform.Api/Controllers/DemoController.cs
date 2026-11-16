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

    public DemoController(DemoSimulator simulator, IngestionWorker worker, INewRelicClient telemetry)
    {
        _simulator = simulator;
        _worker = worker;
        _telemetry = telemetry;
    }

    /// <summary>Inject a fresh anomaly and run one ingestion pass so a case appears live.</summary>
    [HttpPost("simulate")]
    [RequireRole("Analyst", "Lead")]
    public async Task<SimulationResult> Simulate(CancellationToken ct)
        => await _simulator.SimulateAuthorizationGapAsync(RequestId(), ct);

    /// <summary>Attack families and their bypass variants, for the Simulate menu.</summary>
    [HttpGet("scenarios")]
    public IReadOnlyList<ScenarioGroup> Scenarios() => DemoSimulator.Catalog;

    /// <summary>Fire a specific bypass variant live (see GET scenarios); legacy family keys still work.</summary>
    [HttpPost("simulate/{scenario}")]
    [RequireRole("Analyst", "Lead")]
    public async Task<ActionResult<SimulationResult>> SimulateScenario(string scenario, CancellationToken ct)
    {
        try { return await _simulator.SimulateScenarioAsync(scenario, RequestId(), ct); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>Run one ingestion pass on demand (idempotent).</summary>
    [HttpPost("run")]
    [RequireRole("Analyst", "Lead")]
    public async Task<object> Run(CancellationToken ct)
        => new { casesCreated = await _worker.RunOnceAsync(ct, RequestId()) };

    /// <summary>The platform's own telemetry (self-observability through the mock New Relic client).</summary>
    [HttpGet("telemetry")]
    public IReadOnlyList<TelemetryEvent> Telemetry([FromQuery] int take = 25) => _telemetry.Recent(take);

    private string RequestId() => HttpContext.Items[Security.RequestTracingMiddleware.ItemKey] as string ?? "req-adhoc";
}
