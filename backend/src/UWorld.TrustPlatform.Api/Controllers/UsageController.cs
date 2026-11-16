using Microsoft.AspNetCore.Mvc;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Api.Controllers;

[ApiController]
[Route("api/usage")]
public sealed class UsageController : ControllerBase
{
    private readonly IUsageLedger _ledger;

    public UsageController(IUsageLedger ledger) => _ledger = ledger;

    /// <summary>The request ledger - every request + both costs. Filter by requestId to see one request end-to-end.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<LedgerEntry>> Get([FromQuery] string? requestId, [FromQuery] int take = 50, CancellationToken ct = default)
        => await _ledger.GetAsync(requestId, take, ct);

    /// <summary>Roll-up: total AI spend, total business dollars at risk, and both broken down by rule.</summary>
    [HttpGet("summary")]
    public async Task<UsageSummary> Summary(CancellationToken ct) => await _ledger.GetSummaryAsync(ct);
}
