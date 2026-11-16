namespace UWorld.TrustPlatform.Domain;

/// <summary>One captured API call (Development only): what came in, what went out, and any exception.</summary>
public sealed class DevRequestEntry
{
    public long Id { get; set; }
    public string RequestId { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string? QueryString { get; set; }
    public string? RequestHeaders { get; set; }
    public string? RequestBody { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseHeaders { get; set; }
    public string? ResponseBody { get; set; }
    public int DurationMs { get; set; }
    public string? Role { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public string? RoutePattern { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? StackTrace { get; set; }
    public DateTime OccurredUtc { get; set; }
}
