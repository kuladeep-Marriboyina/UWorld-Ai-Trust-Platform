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
    /// <summary>Safety-net sweep. Detection is event-driven (every signup/login/checkout triggers a pass); this catches anything written by other systems.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>What one detection pass did.</summary>
public sealed record PassResult(string RequestId, string Trigger, int RowsEvaluated, int CasesEvaluated, IReadOnlyList<CaseSaveOutcome> Changes, int DurationMs, DateTime CompletedUtc)
{
    public int Created => Changes.Count(c => c.Kind == "Created");
    public int Updated => Changes.Count(c => c.Kind is "Updated" or "Reopened");
}

/// <summary>Notified after every pass that changed something (the API streams it to analysts).</summary>
public interface IDetectionObserver
{
    Task OnPassAsync(PassResult result, CancellationToken ct);
}

/// <summary>
/// The platform's heartbeat: loads the current signal set, runs the rule engine, and persists cases.
/// Passes are serialized so a burst of storefront events never races two evaluations against each other;
/// persistence is idempotent (CaseKey) and evolving attacks update their existing case.
/// </summary>
public sealed class IngestionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly DetectionRuleEngine _engine;
    private readonly INewRelicClient _telemetry;
    private readonly IngestionOptions _options;
    private readonly ILogger<IngestionWorker> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IngestionWorker(IServiceProvider services, DetectionRuleEngine engine, INewRelicClient telemetry,
        IngestionOptions options, ILogger<IngestionWorker> logger)
    {
        _services = services;
        _engine = engine;
        _telemetry = telemetry;
        _options = options;
        _logger = logger;
    }

    public PassResult? LastPass { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(_options.StartupDelay, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunPassAsync("scheduled sweep", stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Ingestion pass failed"); }

            try { await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Back-compat: run a pass and return how many cases were created.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct, string? requestId = null)
        => (await RunPassAsync("manual", ct, requestId).ConfigureAwait(false)).Created;

    public async Task<PassResult> RunPassAsync(string trigger, CancellationToken ct, string? requestId = null)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _services.CreateScope();
            var signalRepo = scope.ServiceProvider.GetRequiredService<ISignalRepository>();
            var caseRepo = scope.ServiceProvider.GetRequiredService<IRiskCaseRepository>();
            var ledger = scope.ServiceProvider.GetRequiredService<IUsageLedger>();

            var sw = Stopwatch.StartNew();
            var signals = await signalRepo.LoadAllAsync(ct).ConfigureAwait(false);
            var reqId = requestId ?? signals.BatchCorrelationId;
            var cases = _engine.Evaluate(signals);

            var changes = new List<CaseSaveOutcome>();
            foreach (var c in cases)
            {
                var outcome = await caseRepo.SaveAsync(c, ct).ConfigureAwait(false);
                if (outcome.Kind == "Unchanged") continue;
                changes.Add(outcome);
                if (outcome.Kind == "Created")
                    await ledger.AddAsync(new LedgerEntry
                    {
                        RequestId = reqId, Kind = "CaseRaised", Operation = c.Title.Length > 200 ? c.Title[..200] : c.Title,
                        CaseId = outcome.CaseId, RuleId = c.RuleId, BusinessImpactUsd = c.BusinessImpactUsd, OccurredUtc = DateTime.UtcNow
                    }, ct).ConfigureAwait(false);
            }
            sw.Stop();

            var rows = signals.Users.Count + signals.ApiLogs.Count + signals.Payments2.Count + signals.UserLogs.Count
                     + signals.Subscriptions.Count + signals.ReferralUsages.Count + signals.AccessEvents.Count;
            var result = new PassResult(reqId, trigger, rows, cases.Count, changes, (int)sw.ElapsedMilliseconds, DateTime.UtcNow);
            LastPass = result;

            if (changes.Count > 0 || trigger != "scheduled sweep")
                await ledger.AddAsync(new LedgerEntry
                {
                    RequestId = reqId, Kind = "IngestionPass",
                    Operation = $"{trigger}: {_engine.Rules.Count} rules over {rows:N0} rows → {result.Created} new, {result.Updated} updated",
                    DurationMs = result.DurationMs, OccurredUtc = DateTime.UtcNow
                }, ct).ConfigureAwait(false);

            await _telemetry.RecordAsync("IngestionPass", new Dictionary<string, object>
            {
                ["requestId"] = reqId, ["trigger"] = trigger, ["rows"] = rows,
                ["casesEvaluated"] = cases.Count, ["casesCreated"] = result.Created, ["casesUpdated"] = result.Updated
            }, ct).ConfigureAwait(false);

            if (changes.Count > 0)
            {
                foreach (var observer in scope.ServiceProvider.GetServices<IDetectionObserver>())
                {
                    try { await observer.OnPassAsync(result, ct).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Detection observer failed"); }
                }
                _logger.LogInformation("Pass {Req} ({Trigger}): {Created} new, {Updated} updated in {Ms} ms", reqId, trigger, result.Created, result.Updated, result.DurationMs);
            }
            return result;
        }
        finally { _gate.Release(); }
    }
}
