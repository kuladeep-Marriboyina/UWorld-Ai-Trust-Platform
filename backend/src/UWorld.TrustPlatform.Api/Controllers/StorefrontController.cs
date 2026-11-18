using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Api.Security;
using UWorld.TrustPlatform.Api.Services;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Detection;
using UWorld.TrustPlatform.Detection.Rules;
using UWorld.TrustPlatform.Domain;
using UWorld.TrustPlatform.Ingestion;

namespace UWorld.TrustPlatform.Api.Controllers;

/// <summary>
/// The simulated customer storefront (signup, login, checkout). It records exactly what the real customer apps
/// record — USERS, API_LOGS, subscriptions, payments — and every call triggers a detection pass immediately,
/// so an attack is flagged while it is happening, not in tomorrow's report.
/// The network identity comes from X-Sim-Ip / X-Sim-Device / X-Sim-Country headers so several browser windows
/// can act as one machine (or as many different people) on a single laptop.
/// The <c>detection</c> block in each response is presenter-only telemetry; a real customer would never see it.
/// </summary>
[ApiController]
[Route("api/storefront")]
public sealed partial class StorefrontController : ControllerBase
{
    private readonly IIdentityRepository _identity;
    private readonly IngestionWorker _worker;
    private readonly ISignalRepository _signals;
    private readonly AccountFarmRule _farm;
    private readonly RuleConfigStore _config;
    private readonly LiveEventHub _hub;

    public StorefrontController(IIdentityRepository identity, IngestionWorker worker, ISignalRepository signals,
        IEnumerable<IDetectionRule> rules, RuleConfigStore config, LiveEventHub hub)
    {
        _identity = identity;
        _worker = worker;
        _signals = signals;
        _farm = rules.OfType<AccountFarmRule>().First();
        _config = config;
        _hub = hub;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")] private static partial Regex EmailFormat();

    private (string Ip, string Device, string? Country, string? Ua) Identity()
    {
        var ip = Request.Headers["X-Sim-Ip"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(ip)) ip = HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
        var device = Request.Headers["X-Sim-Device"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(device)) device = "dev-unknown";
        var country = Request.Headers["X-Sim-Country"].FirstOrDefault();
        var ua = Request.Headers["X-Sim-UA"].FirstOrDefault() ?? Request.Headers.UserAgent.FirstOrDefault();
        return (ip.Trim(), device.Trim(), string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant(), ua);
    }

    [HttpGet("packages")]
    public async Task<IReadOnlyList<PackageInfo>> Packages(CancellationToken ct) => await _identity.GetPackagesAsync(ct);

    [HttpGet("identity")]
    public object WhoAmI()
    {
        var id = Identity();
        return new { ip = id.Ip, device = id.Device, country = id.Country, userAgent = id.Ua };
    }

    [HttpPost("signup")]
    public async Task<ActionResult<object>> Signup([FromBody] SignupRequest body, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var id = Identity();
        var email = (body.Email ?? "").Trim().ToLowerInvariant();
        string? error = null;
        if (string.IsNullOrWhiteSpace(body.FirstName) || string.IsNullOrWhiteSpace(body.LastName)) error = "First and last name are required.";
        else if (!EmailFormat().IsMatch(email)) error = "Enter a valid email address.";
        else if ((body.Password ?? "").Length < 6) error = "Password must be at least 6 characters.";
        if (error != null)
        {
            await Log(null, email, "POST", "/api/auth/register", 400, "Rejected", id, sw, ct);
            return BadRequest(new { error });
        }

        var userId = await _identity.CreateUserAsync(new NewUser(email, body.FirstName!, body.LastName!, body.Phone, body.Country ?? id.Country,
            id.Ip, id.Device, id.Ua, body.Password!, body.Source ?? "Web"), ct);
        if (userId is null)
        {
            await Log(null, email, "POST", "/api/auth/register", 409, "Rejected", id, sw, ct);
            _hub.Publish("signup-rejected", "low", $"Duplicate signup attempt for {email}", $"from {id.Ip}", ip: id.Ip);
            return Conflict(new { error = "An account with this email already exists." });
        }
        await Log(userId, email, "POST", "/api/auth/register", 201, "Success", id, sw, ct);
        _hub.Publish("signup", "info", $"New account #{userId}: {email}", $"from {id.Ip} · device {id.Device}", ip: id.Ip,
            data: new { userId, email, device = id.Device, country = body.Country ?? id.Country });

        var trace = await DetectAsync($"signup #{userId}", userId.Value, ct);
        return StatusCode(201, new { userId, email, welcome = $"Welcome, {body.FirstName}!", detection = trace });
    }

    [HttpPost("login")]
    public async Task<ActionResult<object>> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var id = Identity();
        var email = (body.Email ?? "").Trim().ToLowerInvariant();
        var user = email.Length > 0 ? await _identity.FindByEmailAsync(email, ct) : null;
        var ok = user is not null && user.Value.PasswordHash == IIdentityRepository.Hash(body.Password ?? "");

        await Log(ok ? user!.Value.UserId : null, email, "POST", "/api/auth/login", ok ? 200 : 401, ok ? "Success" : "Failed", id, sw, ct);
        _hub.Publish(ok ? "login" : "login-failed", ok ? "info" : "low",
            ok ? $"Login: {email}" : $"Failed login for {(email.Length > 0 ? email : "(blank)")}", $"from {id.Ip}", ip: id.Ip);

        var trace = await DetectAsync(ok ? $"login {email}" : $"failed login {email}", user?.UserId, ct);
        if (!ok) return StatusCode(401, new { error = "Email or password is incorrect.", detection = trace });
        return new { userId = user!.Value.UserId, email = user.Value.Email, detection = trace };
    }

    [HttpPost("subscribe")]
    public async Task<ActionResult<object>> Subscribe([FromBody] SubscribeRequest body, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var id = Identity();
        try
        {
            var result = await _identity.SubscribeAsync(new SubscribeWrite(body.UserId, body.PackageId, body.Trial, body.DiscountCode, body.CardNumber, id.Ip, id.Country, id.Device), ct);
            await Log(body.UserId, null, "POST", "/api/orders/subscribe", 201, "Success", id, sw, ct);
            _hub.Publish("subscribe", "info",
                body.Trial ? $"Account #{body.UserId} started a free trial of {result.Package}" : $"Account #{body.UserId} bought {result.Package} for ${result.Charged:N2}",
                result.CodeApplied != null ? $"code {result.CodeApplied} (−${result.Discount:N2})" : $"from {id.Ip}", ip: id.Ip);
            var trace = await DetectAsync($"subscription by #{body.UserId}", body.UserId, ct);
            return StatusCode(201, new { subscription = result, detection = trace });
        }
        catch (ArgumentException ex)
        {
            await Log(body.UserId, null, "POST", "/api/orders/subscribe", 400, "Rejected", id, sw, ct);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Open protected QBank content. channel: "app" (checks entitlement correctly), "export" (Download-PDF endpoint that
    /// ignores a deny — the bug), "cache" (mobile/CDN cache that never re-checks). Returns 403 only when nothing was served.
    /// </summary>
    [HttpPost("content")]
    public async Task<ActionResult<object>> Content([FromBody] ContentRequest body, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var id = Identity();
        var r = await _identity.AccessContentAsync(new ContentAccessWrite(body.UserId, body.PackageId, body.QuestionId <= 0 ? 1001 : body.QuestionId,
            body.Channel ?? "app", id.Ip, id.Device, HttpContext.RequestId()), ct);
        var served = r.BytesReturned > 0;
        var status = served ? 200 : 403;
        await Log(body.UserId, null, "GET", "/api" + r.Resource, status, served ? "Success" : "Rejected", id, sw, ct);
        if (!body.Quiet)
            _hub.Publish(served ? "content" : "content-denied", r.Entitled || !served ? "info" : "low",
                served ? $"Account #{body.UserId} received {r.BytesReturned:N0} bytes from {r.Resource}" : $"Account #{body.UserId} denied {r.Resource}",
                $"authorization {r.AuthResult} · {r.Channel} · from {id.Ip}", ip: id.Ip);
        var trace = await DetectAsync($"content {r.Channel} by #{body.UserId}", body.UserId, ct);
        var payload = new
        {
            authorization = r.AuthResult, entitled = r.Entitled, subscription = r.SubscriptionState ?? "none",
            bytesReturned = r.BytesReturned, resource = r.Resource, channel = r.Channel, correlationId = r.CorrelationId,
            message = served
                ? (r.Entitled ? "Here's your question." : r.Channel == "export" ? "Export ready — 2.4 MB PDF of the full question set." : "Loaded from offline cache.")
                : "Subscribe to unlock this question bank.",
            detection = trace
        };
        return StatusCode(status, payload);
    }

    private Task Log(int? userId, string? email, string method, string endpoint, int status, string outcome,
        (string Ip, string Device, string? Country, string? Ua) id, Stopwatch sw, CancellationToken ct)
        => _identity.LogApiAsync(new ApiLogWrite(HttpContext.RequestId(), userId, email, method, endpoint, status, outcome,
            id.Ip, id.Country, id.Ua, id.Device, (int)sw.ElapsedMilliseconds), ct);

    /// <summary>Run detection now, then report what the engine thinks of the identity that just acted.</summary>
    private async Task<object> DetectAsync(string trigger, int? userId, CancellationToken ct)
    {
        var pass = await _worker.RunPassAsync(trigger, ct, HttpContext.RequestId());
        var signals = await _signals.LoadAllAsync(ct);
        var id = Identity();

        var cluster = userId is int uid ? _farm.Assess(signals).FirstOrDefault(c => c.Accounts.Any(a => a.UserId == uid)) : null;
        var since = DateTime.UtcNow.AddMinutes(-_config.Num("credential-stuffing", "windowMinutes", 15));
        var failures = signals.ApiLogs.Where(l => l.IPAddress == id.Ip && l.Outcome == "Failed" && l.Endpoint.Contains("/auth/login") && l.DateCreated >= since).ToList();

        var alertScore = (decimal)_config.Num("account-farm", "alertScore", 55);
        var minAccounts = _config.Int("account-farm", "minAccounts", 3);
        var touched = pass.Changes.Select(o => new
        {
            caseId = o.CaseId, kind = o.Kind, title = o.Case.Title, severity = o.Case.Severity.ToString(), score = o.Case.Score,
            ruleId = o.Case.RuleId, latencyMs = o.Case.DetectionLatencyMs, change = o.Change
        }).ToList();

        var verdict = touched.Count > 0 ? "alert" : (cluster is not null && cluster.Score >= 20) || failures.Count >= 3 ? "watching" : "clean";
        return new
        {
            verdict,
            passMs = pass.DurationMs,
            rowsEvaluated = pass.RowsEvaluated,
            cases = touched,
            accountFarm = cluster is null ? null : new
            {
                score = cluster.Score, alertScore, minAccounts, accounts = cluster.Accounts.Count, corroborated = cluster.Corroborated,
                wouldAlert = cluster.WouldAlert, userIds = cluster.Accounts.Select(a => a.UserId), template = cluster.Emails.DominantTemplate,
                factors = cluster.Factors.Select(f => new { f.Signal, f.Weight, f.Rationale })
            },
            credentialStuffing = new
            {
                failuresFromIp = failures.Count,
                distinctAccounts = failures.Select(f => f.Email).Distinct().Count(),
                minFailures = _config.Int("credential-stuffing", "minFailures", 8),
                minDistinctAccounts = _config.Int("credential-stuffing", "minDistinctAccounts", 5),
            }
        };
    }
}

public sealed record SignupRequest(string? FirstName, string? LastName, string? Email, string? Password, string? Phone, string? Country, string? Source);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record SubscribeRequest(int UserId, short PackageId, bool Trial, string? DiscountCode, string? CardNumber);
public sealed record ContentRequest(int UserId, short PackageId, int QuestionId, string? Channel, bool Quiet = false);
