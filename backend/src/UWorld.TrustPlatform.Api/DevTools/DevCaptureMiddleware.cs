using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.DevTools;

/// <summary>
/// Development only. Records every /api call — request headers and body, response status, headers and
/// body, duration, the controller action that handled it, and any unhandled exception with its stack
/// trace — so the Dev info tab can show it and the code investigator can trace it to the source.
/// An unhandled exception is turned into a JSON 500 that carries the request id, instead of an empty page.
/// </summary>
public sealed class DevCaptureMiddleware
{
    private const int MaxBodyChars = 64 * 1024;
    private static readonly string[] KeptRequestHeaders = { "X-Role", "X-Request-Id", "Content-Type", "Accept", "Origin", "Referer", "User-Agent" };
    private static readonly string[] KeptResponseHeaders = { "Content-Type", "X-Request-Id", "Location" };

    private readonly RequestDelegate _next;
    private readonly ILogger<DevCaptureMiddleware> _log;

    public DevCaptureMiddleware(RequestDelegate next, ILogger<DevCaptureMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx, IDevRequestLog store)
    {
        var path = ctx.Request.Path;
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/dev") || HttpMethods.IsOptions(ctx.Request.Method))
        {
            await _next(ctx);
            return;
        }

        ctx.Request.EnableBuffering();
        var requestBody = await ReadBodyAsync(ctx.Request.Body);
        ctx.Request.Body.Position = 0;

        var originalBody = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        var sw = Stopwatch.StartNew();
        Exception? error = null;
        var responseBody = "";
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            error = ex;
            _log.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, path);
            buffer.SetLength(0);
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "application/json";
            var requestId = ctx.Items[RequestTracingMiddleware.ItemKey] as string;
            await JsonSerializer.SerializeAsync(buffer, new
            {
                error = "Internal server error",
                exception = ex.GetType().Name,
                message = ex.Message,
                requestId,
                hint = "Open the Dev info tab and use Find the bug on this request."
            });
        }
        finally
        {
            sw.Stop();
            buffer.Position = 0;
            responseBody = await ReadBodyAsync(buffer);
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
            ctx.Response.Body = originalBody;
        }

        try
        {
            var endpoint = ctx.GetEndpoint();
            var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
            var root = error is null ? null : Unwrap(error);
            await store.AddAsync(new DevRequestEntry
            {
                RequestId = ctx.Items[RequestTracingMiddleware.ItemKey] as string ?? "req-unknown",
                Method = ctx.Request.Method,
                Path = path.Value ?? "",
                QueryString = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : null,
                RequestHeaders = Headers(ctx.Request.Headers, KeptRequestHeaders),
                RequestBody = string.IsNullOrEmpty(requestBody) ? null : requestBody,
                StatusCode = ctx.Response.StatusCode,
                ResponseHeaders = Headers(ctx.Response.Headers, KeptResponseHeaders),
                ResponseBody = string.IsNullOrEmpty(responseBody) ? null : responseBody,
                DurationMs = (int)sw.ElapsedMilliseconds,
                Role = ctx.Items[RoleContextMiddleware.ItemKey] as string ?? ctx.Request.Headers["X-Role"].FirstOrDefault(),
                Controller = action?.ControllerTypeInfo.FullName,
                Action = action?.MethodInfo.Name,
                RoutePattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText,
                ExceptionType = root?.GetType().FullName,
                ExceptionMessage = root?.Message,
                StackTrace = error?.ToString(),
                OccurredUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // Capturing must never break the real request.
            _log.LogWarning(ex, "Could not store dev request log for {Path}", path);
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is AggregateException { InnerException: not null } || (ex.InnerException is not null && ex is System.Reflection.TargetInvocationException))
            ex = ex.InnerException!;
        return ex;
    }

    private static async Task<string> ReadBodyAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var buffer = new char[MaxBodyChars + 1];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        var text = new string(buffer, 0, Math.Min(read, MaxBodyChars));
        return read > MaxBodyChars ? text + "\n…[truncated at 64 KB]" : text;
    }

    private static string? Headers(IHeaderDictionary headers, string[] keep)
    {
        var kept = keep.Where(headers.ContainsKey).ToDictionary(k => k, k => headers[k].ToString());
        return kept.Count == 0 ? null : JsonSerializer.Serialize(kept);
    }
}
