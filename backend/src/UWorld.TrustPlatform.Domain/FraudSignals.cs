namespace UWorld.TrustPlatform.Domain;

/// <summary>
/// Phase-2 signals mirroring the real UWorld schema (USER_PAYMENTS, BRAINTREE_TRANSACTIONS,
/// NCLEX_REFERRAL_CODES, USER_SUBSCRIPTIONS, USER_LOGS …). Read-only snapshots.
/// </summary>
public sealed record PaymentRecord(
    int PaymentId, int UserId, decimal FinalAmount, decimal TransactionAmount, decimal DiscountAmount,
    string? TransactionId, byte PaymentStatus, byte? GatewayId, byte PaymentModeId, byte? SourceId,
    byte? PaymentTypeId, string? IPAddress, string? CountryCode, DateTime DateCreated,
    string? CardFingerprint = null, string? CardLast4 = null);

/// <summary>USERS — one row per account, with the network/device identity it was created from.</summary>
public sealed record UserRecord(
    int UserId, string Email, string FirstName, string LastName, string? CountryCode,
    string RegistrationIp, string DeviceFingerprint, string? UserAgent, string SignupSource, DateTime DateCreated);

/// <summary>API_LOGS — every customer-facing API call (register, login, subscribe, apply code…).</summary>
public sealed record ApiLogRecord(
    long Id, string RequestId, int? UserId, string? Email, string Method, string Endpoint, int StatusCode,
    string Outcome, string IPAddress, string? CountryCode, string? UserAgent, string? DeviceFingerprint, DateTime DateCreated);

/// <summary>PACKAGES — catalog with list price (drives factual $ impact).</summary>
public sealed record PackageRecord(short PackageId, string Name, decimal ListPrice, int DurationDays, int TrialDays);

public sealed record BraintreeTxn(
    string TransactionId, int? PaymentId, string TransactionStatus, decimal? Amount,
    bool IsMismatch, bool IsVerified, bool IsFraudDetected, DateTime DateCreated);

public sealed record PaymentLineDetail(
    int Id, int PaymentId, short PackageId, int? CourseId, string? DiscountCode,
    decimal BaseAmount, decimal DiscountAmount, decimal FinalAmount);

public sealed record ReferralCode(
    int ReferralId, string Code, int Limit, int Used, DateTime ExpirationUtc,
    int? DiscountGroupId, byte BehaviourTypeId, decimal Percent);

public sealed record ReferralUsage(int Id, string Code, int UserId, int? PaymentId, DateTime DateUsed);

public sealed record SubscriptionRecord(
    int SubscriptionId, int UserId, int CourseId, short PackageId, bool Active,
    DateTime DateActivated, DateTime DateExpiration, DateTime? DateFirstAccessed);

public sealed record UserLog(
    int Id, int UserId, int? SubscriptionId, string IPAddress, string? CountryCode,
    byte? DeviceTypeId, string? LogType, string? Url, DateTime DateCreated);

public sealed record BlockedIp(int Id, string IPAddress, string? Reason);

public sealed record Country(int CountryId, string Name, string IsoCode, bool IsEmbargoed);

/// <summary>GA_DISCOUNTS — which packages a discount group (and therefore a code) is valid for.</summary>
public sealed record GaDiscount(
    int DiscountId, int DiscountGroupId, short PackageId, decimal DiscountAmount, bool IsPercent,
    byte BehaviourTypeId, decimal? FloorValue);
