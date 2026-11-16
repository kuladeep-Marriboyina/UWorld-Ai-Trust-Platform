using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Ingestion;

public sealed class IngestionOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// The platform's heartbeat: on an interval it loads the current signal set, runs the deterministic
/// rule engine, and persists any new RiskCases. Persistence is idempotent (CaseKey), so repeated
/// passes over unchanged data never create duplicates.
/// </summary>
public sealed class IngestionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly DetectionRuleEngine _engine;
    private readonly INewRelicClient _telemetry;
    private readonly IngestionOptions _options;
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(
        IServiceProvider services,
        DetectionRuleEngine engine,
        INewRelicClient telemetry,
        IngestionOptions options,
        ILogger<IngestionWorker> logger)
    {
        _services = services;
        _engine = engine;
        _telemetry = telemetry;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(_options.StartupDelay, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ingestion pass failed");
            }

            try { await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <param name="requestId">Ties this pass (and the cases it raises) to a request in the ledger. Defaults to the batch id.</param>
    public async Task<int> RunOnceAsync(CancellationToken ct, string? requestId = null)
    {
        using var scope = _services.CreateScope();
        var signalRepo = scope.ServiceProvider.GetRequiredService<ISignalRepository>();
        var caseRepo = scope.ServiceProvider.GetRequiredService<IRiskCaseRepository>();
        var ledger = scope.ServiceProvider.GetRequiredService<IUsageLedger>();

        var sw = Stopwatch.StartNew();
        var signals = await signalRepo.LoadAllAsync(ct).ConfigureAwait(false);
        var reqId = requestId ?? signals.BatchCorrelationId;
        var cases = _engine.Evaluate(signals);

        var created = 0;
        foreach (var c in cases)
        {
            var caseId = await caseRepo.SaveAsync(c, ct).ConfigureAwait(false);
            if (caseId is null) continue;   // idempotent: already existed
            created++;
            await ledger.AddAsync(new LedgerEntry
            {
                RequestId = reqId, Kind = "CaseRaised", Operation = c.Title,
                CaseId = caseId, RuleId = c.RuleId, BusinessImpactUsd = c.BusinessImpactUsd,
                DurationMs = 0, OccurredUtc = DateTime.UtcNow
            }, ct).ConfigureAwait(false);
        }
        sw.Stop();

        await ledger.AddAsync(new LedgerEntry
        {
            RequestId = reqId, Kind = "IngestionPass",
            Operation = $"evaluate {_engine.Rules.Count} rules over {signals.AccessEvents.Count} access events",
            DurationMs = (int)sw.ElapsedMilliseconds, OccurredUtc = DateTime.UtcNow
        }, ct).ConfigureAwait(false);

        await _telemetry.RecordAsync("IngestionPass", new Dictionary<string, object>
        {
            ["requestId"] = reqId,
            ["signalsAccess"] = signals.AccessEvents.Count,
            ["casesEvaluated"] = cases.Count,
            ["casesCreated"] = created
        }, ct).ConfigureAwait(false);

        _logger.LogInformation("Ingestion pass {Req}: evaluated {Evaluated} cases, {Created} new",
            reqId, cases.Count, created);
        return created;
    }
}
