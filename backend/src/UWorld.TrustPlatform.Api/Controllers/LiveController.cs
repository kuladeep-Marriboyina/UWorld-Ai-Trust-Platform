using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Api.Services;

namespace UWorld.TrustPlatform.Api.Controllers;

/// <summary>Live feed: Server-Sent Events of customer activity and detection outcomes.</summary>
[ApiController]
[Route("api/live")]
public sealed class LiveController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly LiveEventHub _hub;
    public LiveController(LiveEventHub hub) => _hub = hub;

    [HttpGet("recent")]
    public IReadOnlyList<LiveEvent> Recent([FromQuery] int take = 60) => _hub.Recent(Math.Clamp(take, 1, 250));

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        var (id, reader) = _hub.Subscribe();
        try
        {
            await Response.WriteAsync($": connected ({_hub.Connected} viewers)\n\n", ct);
            await Response.Body.FlushAsync(ct);
            using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var hbTask = heartbeat.WaitForNextTickAsync(ct).AsTask();
            while (!ct.IsCancellationRequested)
            {
                var readTask = reader.WaitToReadAsync(ct).AsTask();
                var done = await Task.WhenAny(readTask, hbTask);
                if (done == hbTask)
                {
                    await Response.WriteAsync(": keep-alive\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    hbTask = heartbeat.WaitForNextTickAsync(ct).AsTask();
                    continue;
                }
                if (!await readTask) break;
                while (reader.TryRead(out var evt))
                    await Response.WriteAsync($"id: {evt.Id}\nevent: message\ndata: {JsonSerializer.Serialize(evt, Json)}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* client went away */ }
        finally { _hub.Unsubscribe(id); }
    }
}
