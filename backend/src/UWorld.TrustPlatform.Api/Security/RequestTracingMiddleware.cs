using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Security;

/// <summary>
/// Gives every request one RequestId (from the X-Request-Id header or freshly minted), echoes it
/// on the response, and records an HttpRequest row in the ledger so cost and activity roll up per
/// request. The same id is reused by the ingestion pass / AI investigation that the request triggers.
/// </summary>
public sealed class RequestTracingMiddleware
{
    public const string ItemKey = "RequestId";
    private readonly RequestDelegate _next;

    public RequestTracingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestId))
            requestId = "req-" + Guid.NewGuid().ToString("N")[..8];
        context.Items[ItemKey] = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            if (context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/api/live")
                && !(HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/dashboard")))
            {
                var ledger = context.RequestServices.GetService<IUsageLedger>();
                if (ledger is not null)
                {
                    await ledger.AddAsync(new LedgerEntry
                    {
                        RequestId = requestId,
                        Kind = "HttpRequest",
                        Operation = $"{context.Request.Method} {context.Request.Path}",
                        DurationMs = (int)sw.ElapsedMilliseconds,
                        OccurredUtc = DateTime.UtcNow
                    });
                }
            }
        }
    }
}
