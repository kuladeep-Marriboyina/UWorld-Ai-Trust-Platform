using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace UWorld.TrustPlatform.Ai;

/// <summary>The result of one model call: parsed JSON plus what it cost.</summary>
public sealed record AiCall(JsonElement Json, string Model, int InputTokens, int OutputTokens, int DurationMs);

public sealed record AiStatus(bool Live, string Provider, string Model, string Detail);

/// <summary>
/// The single gateway to the language model. Every call asks for schema-constrained JSON, so outputs are
/// machine-checkable; when no credentials are configured (or a call fails) it returns null and the caller
/// falls back to deterministic logic — the platform never depends on the model to detect anything.
/// </summary>
public interface IAiProvider
{
    AiStatus Status { get; }
    Task<AiCall?> JsonAsync(string system, string user, object schema, string effort = "low", int maxTokens = 4000, CancellationToken ct = default);
}

public sealed class ClaudeProvider : IAiProvider
{
    public const string DefaultModel = "claude-opus-5-5";
    private readonly AnthropicClient? _client;
    private readonly ILogger<ClaudeProvider> _log;
    private readonly string _keySource = "none";
    private string? _lastError;

    public string Model { get; }

    /// <summary>
    /// Key lookup order: configuration "Anthropic:ApiKey" (user-secrets / appsettings / env var Anthropic__ApiKey),
    /// then the standard ANTHROPIC_API_KEY / ANTHROPIC_AUTH_TOKEN environment variables.
    /// Prefer user-secrets: they live outside the repo, so the key can't be committed by accident.
    /// </summary>
    public ClaudeProvider(ILogger<ClaudeProvider> log, IConfiguration config)
    {
        _log = log;
        Model = string.IsNullOrWhiteSpace(config["Anthropic:Model"]) ? DefaultModel : config["Anthropic:Model"]!.Trim();
        var configKey = config["Anthropic:ApiKey"];
        try
        {
            if (!string.IsNullOrWhiteSpace(configKey))
            {
                _client = new AnthropicClient { ApiKey = configKey.Trim() };
                _keySource = "configuration (Anthropic:ApiKey)";
            }
            else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))
                  || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")))
            {
                _client = new AnthropicClient();
                _keySource = "environment variable";
            }
        }
        catch (Exception ex) { _lastError = ex.Message; _log.LogWarning(ex, "Claude client could not be created; using deterministic fallback."); }
        _log.LogInformation(_client is null ? "AI: no Anthropic key found — using deterministic fallbacks." : "AI: Claude enabled ({Model}, key from {Source}).", Model, _keySource);
    }

    public AiStatus Status => _client is null
        ? new AiStatus(false, "deterministic", "rules-based fallback", _lastError ??
            "No Anthropic API key found. Run: dotnet user-secrets set \"Anthropic:ApiKey\" \"<key>\" in backend/src/UWorld.TrustPlatform.Api, then restart the API. Until then every AI feature uses its evidence-only fallback.")
        : new AiStatus(true, "claude", Model, _lastError is null ? $"Claude is configured (key from {_keySource})." : $"Configured, but the last call failed: {_lastError}");

    public async Task<AiCall?> JsonAsync(string system, string user, object schema, string effort = "low", int maxTokens = 4000, CancellationToken ct = default)
    {
        if (_client is null) return null;
        var sw = Stopwatch.StartNew();
        try
        {
            var schemaDict = JsonSerializer.SerializeToElement(schema).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            var response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = maxTokens,
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                System = system,
                OutputConfig = new BetaOutputConfig
                {
                    Effort = effort switch { "high" => Effort.High, "medium" => Effort.Medium, _ => Effort.Low },
                    Format = new BetaJsonOutputFormat { Schema = schemaDict },
                },
                Messages = [new BetaUserMessageParam { Content = user }],
            }, cancellationToken: ct).ConfigureAwait(false);
            sw.Stop();

            if (response.StopReason == "refusal")
            {
                _lastError = "model declined the request";
                return null;
            }
            var text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            using var doc = JsonDocument.Parse(text);
            _lastError = null;
            return new AiCall(doc.RootElement.Clone(), Model, (int)response.Usage.InputTokens, (int)response.Usage.OutputTokens, (int)sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _lastError = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            _log.LogWarning(ex, "Claude call failed; falling back to deterministic output.");
            return null;
        }
    }
}

/// <summary>Small helpers for building JSON schemas for structured outputs.</summary>
public static class Schema
{
    public static object Obj(Dictionary<string, object> props) => new Dictionary<string, object>
    {
        ["type"] = "object", ["properties"] = props, ["required"] = props.Keys.ToArray(), ["additionalProperties"] = false
    };
    public static object Str(string? description = null) => description is null ? new { type = "string" } : new { type = "string", description };
    public static object Num() => new { type = "number" };
    public static object Int() => new { type = "integer" };
    public static object Bool() => new { type = "boolean" };
    public static object Arr(object items) => new { type = "array", items };
    public static object Enum(params string[] values) => new { type = "string", @enum = values };
    public static object NullableStr() => new { type = new[] { "string", "null" } };
    public static object NullableNum() => new { type = new[] { "number", "null" } };
    public static object NullableInt() => new { type = new[] { "integer", "null" } };
}
