using Microsoft.Extensions.Logging;

namespace UWorld.TrustPlatform.Ingestion;

public sealed record TelemetryEvent(string Name, DateTime OccurredUtc, IReadOnlyDictionary<string, object> Attributes);

/// <summary>
/// Abstraction over New Relic. The mock records the platform's own telemetry (self-observability)
/// and can replay synthetic events. A real NRQL-backed client slots in behind this interface.
/// </summary>
public interface INewRelicClient
{
    Task RecordAsync(string eventName, IReadOnlyDictionary<string, object> attributes, CancellationToken ct = default);
    IReadOnlyList<TelemetryEvent> Recent(int take = 50);
}

public sealed class MockNewRelicClient : INewRelicClient
{
    private readonly ILogger<MockNewRelicClient> _logger;
    private readonly object _gate = new();
    private readonly LinkedList<TelemetryEvent> _buffer = new();

    public MockNewRelicClient(ILogger<MockNewRelicClient> logger) => _logger = logger;

    public Task RecordAsync(string eventName, IReadOnlyDictionary<string, object> attributes, CancellationToken ct = default)
    {
        var evt = new TelemetryEvent(eventName, DateTime.UtcNow, attributes);
        lock (_gate)
        {
            _buffer.AddFirst(evt);
            while (_buffer.Count > 200) _buffer.RemoveLast();
        }
        _logger.LogInformation("[NewRelic] {Event} {Attributes}", eventName, string.Join(", ", attributes.Select(a => $"{a.Key}={a.Value}")));
        return Task.CompletedTask;
    }

    public IReadOnlyList<TelemetryEvent> Recent(int take = 50)
    {
        lock (_gate) return _buffer.Take(take).ToList();
    }
}
