using System.Collections.Concurrent;
using System.Threading.Channels;
using UWorld.TrustPlatform.Domain;
using UWorld.TrustPlatform.Ingestion;

namespace UWorld.TrustPlatform.Api.Services;

/// <summary>One thing that just happened, pushed to every open analyst screen.</summary>
public sealed record LiveEvent(
    long Id, string Type, string Level, string Title, string? Detail, DateTime Utc,
    int? CaseId = null, string? RuleId = null, string? Ip = null, decimal? Score = null, object? Data = null);

/// <summary>
/// In-process pub/sub for the live feed. Customer events (signups, logins, checkouts) and detection outcomes
/// (case created / grew / reopened) fan out to every connected Server-Sent-Events client within milliseconds.
/// </summary>
public sealed class LiveEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<LiveEvent>> _subscribers = new();
    private readonly LinkedList<LiveEvent> _recent = new();
    private readonly object _gate = new();
    private long _seq;

    public int Connected => _subscribers.Count;

    public LiveEvent Publish(string type, string level, string title, string? detail = null,
        int? caseId = null, string? ruleId = null, string? ip = null, decimal? score = null, object? data = null)
    {
        var evt = new LiveEvent(Interlocked.Increment(ref _seq), type, level, title, detail, DateTime.UtcNow, caseId, ruleId, ip, score, data);
        lock (_gate)
        {
            _recent.AddFirst(evt);
            while (_recent.Count > 250) _recent.RemoveLast();
        }
        foreach (var ch in _subscribers.Values) ch.Writer.TryWrite(evt);
        return evt;
    }

    /// <summary>Back-fill the feed with recent history from the database (oldest first) so it isn't blank after a restart.</summary>
    public void Hydrate(IEnumerable<(string Type, string Level, string Title, string? Detail, DateTime Utc, int? CaseId, string? Ip)> history)
    {
        lock (_gate)
        {
            foreach (var h in history.OrderBy(h => h.Utc))
                _recent.AddFirst(new LiveEvent(Interlocked.Increment(ref _seq), h.Type, h.Level, h.Title, h.Detail, DateTime.SpecifyKind(h.Utc, DateTimeKind.Utc), h.CaseId, null, h.Ip));
            while (_recent.Count > 250) _recent.RemoveLast();
        }
    }

    public IReadOnlyList<LiveEvent> Recent(int take)
    {
        lock (_gate) return _recent.Take(take).ToList();
    }

    public (Guid Id, ChannelReader<LiveEvent> Reader) Subscribe()
    {
        var ch = Channel.CreateBounded<LiveEvent>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
        var id = Guid.NewGuid();
        _subscribers[id] = ch;
        return (id, ch.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var ch)) ch.Writer.TryComplete();
    }
}

/// <summary>Turns every detection pass that changed something into live alerts.</summary>
public sealed class LiveDetectionObserver : IDetectionObserver
{
    private readonly LiveEventHub _hub;
    public LiveDetectionObserver(LiveEventHub hub) => _hub = hub;

    public Task OnPassAsync(PassResult result, CancellationToken ct)
    {
        foreach (var o in result.Changes)
        {
            var c = o.Case;
            var level = c.Severity.ToString().ToLowerInvariant();
            var ip = c.Entities.FirstOrDefault(e => e.Type == "ip")?.Value;
            var data = new
            {
                severity = c.Severity.ToString(), c.Category, c.BusinessImpactUsd, c.DetectionLatencyMs, c.Confidence,
                factors = c.ScoreBreakdown.Select(f => f.Signal).ToList(), trigger = result.Trigger
            };
            switch (o.Kind)
            {
                case "Created":
                    _hub.Publish("case-created", level, c.Title,
                        c.DetectionLatencyMs is int ms ? $"Detected {Durations.Short(ms)} after the triggering event · {c.RuleId}" : $"Found on backfill · {c.RuleId}",
                        o.CaseId, c.RuleId, ip, c.Score, data);
                    break;
                case "Updated":
                    _hub.Publish("case-updated", level, $"Case #{o.CaseId} grew: {c.Title}", o.Change, o.CaseId, c.RuleId, ip, c.Score, data);
                    break;
                case "Reopened":
                    _hub.Publish("case-reopened", level, $"Case #{o.CaseId} reopened — new evidence after resolution", o.Change, o.CaseId, c.RuleId, ip, c.Score, data);
                    break;
            }
        }
        return Task.CompletedTask;
    }
}
