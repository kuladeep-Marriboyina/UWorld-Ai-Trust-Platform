namespace UWorld.TrustPlatform.Domain;

/// <summary>
/// Normalized signals the ingestion worker pulls from SQL Server / New Relic.
/// These are read-only snapshots - the platform never writes back to them.
/// </summary>
public sealed record Account(int AccountId, string Email, int? LinkedAccountGroupId, DateTime CreatedUtc);

public sealed record OrderSignal(
    int OrderId, int AccountId, int PackageId, string OrderType,
    decimal ListPrice, decimal PaidAmount, decimal PolicyMinPrice, DateTime OrderUtc);

public sealed record PaymentSignal(
    int PaymentId, int OrderId, decimal Amount, string Status, DateTime PaidUtc);

public sealed record DiscountSignal(
    int DiscountId, string Code, int OrderId, int AccountId, decimal Percent, DateTime AppliedUtc);

public sealed record EntitlementSignal(
    int EntitlementId, int AccountId, int PackageId, string Status,
    DateTime StartUtc, DateTime EndUtc, DateTime? RefundedUtc)
{
    /// <summary>Was this entitlement valid at the moment the content was accessed (event-time, not current state)?</summary>
    public bool IsValidAt(DateTime whenUtc) =>
        whenUtc >= StartUtc
        && whenUtc <= EndUtc
        && (RefundedUtc is null || whenUtc < RefundedUtc)
        && !string.Equals(Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
}

public sealed record AuthOutcomeSignal(
    int AuthId, int AccountId, string Resource, string AuthResult, string CorrelationId, DateTime OccurredUtc);

public sealed record AccessEventSignal(
    int AccessId, int AccountId, string Resource, int? PackageId, int? QuestionId, string SessionId,
    long ContentReturnedBytes, string IpAddress, string DeviceId, string CorrelationId, DateTime OccurredUtc);

public sealed record AssetInventoryItem(int AssetId, string Name, string Component, string Version);

public sealed record CveRecord(string CveId, string Component, string AffectedVersion, string Severity, DateTime PublishedUtc);

public sealed record DeploymentSignal(int DeploymentId, string Service, string Version, DateTime DeployedUtc);

/// <summary>
/// Everything the rule engine needs for one evaluation pass, carrying a correlation id
/// so a whole ingestion batch can be traced end-to-end.
/// </summary>
public sealed class SignalSet
{
    public string BatchCorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime EvaluatedUtc { get; init; } = DateTime.UtcNow;

    public IReadOnlyList<Account> Accounts { get; init; } = Array.Empty<Account>();
    public IReadOnlyList<OrderSignal> Orders { get; init; } = Array.Empty<OrderSignal>();
    public IReadOnlyList<PaymentSignal> Payments { get; init; } = Array.Empty<PaymentSignal>();
    public IReadOnlyList<DiscountSignal> Discounts { get; init; } = Array.Empty<DiscountSignal>();
    public IReadOnlyList<EntitlementSignal> Entitlements { get; init; } = Array.Empty<EntitlementSignal>();
    public IReadOnlyList<AuthOutcomeSignal> AuthOutcomes { get; init; } = Array.Empty<AuthOutcomeSignal>();
    public IReadOnlyList<AccessEventSignal> AccessEvents { get; init; } = Array.Empty<AccessEventSignal>();
    public IReadOnlyList<AssetInventoryItem> Assets { get; init; } = Array.Empty<AssetInventoryItem>();
    public IReadOnlyList<CveRecord> Cves { get; init; } = Array.Empty<CveRecord>();
    public IReadOnlyList<DeploymentSignal> Deployments { get; init; } = Array.Empty<DeploymentSignal>();

    // Phase-2 fraud signals (real-schema mirror)
    public IReadOnlyList<PaymentRecord> Payments2 { get; init; } = Array.Empty<PaymentRecord>();
    public IReadOnlyList<BraintreeTxn> BraintreeTxns { get; init; } = Array.Empty<BraintreeTxn>();
    public IReadOnlyList<PaymentLineDetail> PaymentDetails { get; init; } = Array.Empty<PaymentLineDetail>();
    public IReadOnlyList<ReferralCode> ReferralCodes { get; init; } = Array.Empty<ReferralCode>();
    public IReadOnlyList<ReferralUsage> ReferralUsages { get; init; } = Array.Empty<ReferralUsage>();
    public IReadOnlyList<SubscriptionRecord> Subscriptions { get; init; } = Array.Empty<SubscriptionRecord>();
    public IReadOnlyList<UserLog> UserLogs { get; init; } = Array.Empty<UserLog>();
    public IReadOnlyList<BlockedIp> BlockedIps { get; init; } = Array.Empty<BlockedIp>();
    public IReadOnlyList<Country> Countries { get; init; } = Array.Empty<Country>();
    public IReadOnlyList<GaDiscount> GaDiscounts { get; init; } = Array.Empty<GaDiscount>();
}
