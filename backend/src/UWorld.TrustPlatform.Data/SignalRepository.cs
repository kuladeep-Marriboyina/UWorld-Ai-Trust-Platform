using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public interface ISignalRepository
{
    Task<SignalSet> LoadAllAsync(CancellationToken ct = default);
}

/// <summary>
/// Pulls every source signal in one pass using raw ADO.NET. Each reader uses a fresh
/// command scoped in a using block so connections are never leaked.
/// </summary>
public sealed class SignalRepository : ISignalRepository
{
    private readonly ISqlConnectionFactory _factory;

    public SignalRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<SignalSet> LoadAllAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);

        return new SignalSet
        {
            Accounts     = await ReadAsync(cn, ct, "SELECT AccountId, Email, LinkedAccountGroupId, CreatedUtc FROM dbo.Accounts",
                r => new Account(r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetDateTime(3))),

            Orders       = await ReadAsync(cn, ct, "SELECT OrderId, AccountId, PackageId, OrderType, ListPrice, PaidAmount, PolicyMinPrice, OrderUtc FROM dbo.Orders",
                r => new OrderSignal(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6), r.GetDateTime(7))),

            Payments     = await ReadAsync(cn, ct, "SELECT PaymentId, OrderId, Amount, Status, PaidUtc FROM dbo.Payments",
                r => new PaymentSignal(r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2), r.GetString(3), r.GetDateTime(4))),

            Discounts    = await ReadAsync(cn, ct, "SELECT DiscountId, Code, OrderId, AccountId, [Percent], AppliedUtc FROM dbo.Discounts",
                r => new DiscountSignal(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetDecimal(4), r.GetDateTime(5))),

            Entitlements = await ReadAsync(cn, ct, "SELECT EntitlementId, AccountId, PackageId, Status, StartUtc, EndUtc, RefundedUtc FROM dbo.Entitlements",
                r => new EntitlementSignal(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetDateTime(4), r.GetDateTime(5), r.IsDBNull(6) ? null : r.GetDateTime(6))),

            AuthOutcomes = await ReadAsync(cn, ct, "SELECT AuthId, AccountId, Resource, AuthResult, CorrelationId, OccurredUtc FROM dbo.AuthOutcomes",
                r => new AuthOutcomeSignal(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDateTime(5))),

            AccessEvents = await ReadAsync(cn, ct, "SELECT AccessId, AccountId, Resource, PackageId, QuestionId, SessionId, ContentReturnedBytes, IpAddress, DeviceId, CorrelationId, OccurredUtc FROM dbo.AccessEvents",
                r => new AccessEventSignal(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4), r.GetString(5), r.GetInt64(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetDateTime(10))),

            Assets       = await ReadAsync(cn, ct, "SELECT AssetId, Name, Component, Version FROM dbo.AssetInventory",
                r => new AssetInventoryItem(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3))),

            Cves         = await ReadAsync(cn, ct, "SELECT CveId, Component, AffectedVersion, Severity, PublishedUtc FROM dbo.Cves",
                r => new CveRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDateTime(4))),

            Deployments  = await ReadAsync(cn, ct, "SELECT DeploymentId, Service, Version, DeployedUtc FROM dbo.Deployments",
                r => new DeploymentSignal(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDateTime(3))),

            Payments2    = await ReadAsync(cn, ct, "SELECT PaymentId, UserId, FinalAmount, TransactionAmount, DiscountAmount, TransactionId, PaymentStatus, GatewayId, PaymentModeId, SourceId, PaymentTypeId, IPAddress, CountryCode, DateCreated, CardFingerprint, CardLast4 FROM dbo.USER_PAYMENTS",
                r => new PaymentRecord(r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2), r.GetDecimal(3), r.GetDecimal(4), r.IsDBNull(5)?null:r.GetString(5), r.GetByte(6), r.IsDBNull(7)?null:r.GetByte(7), r.GetByte(8), r.IsDBNull(9)?null:r.GetByte(9), r.IsDBNull(10)?null:r.GetByte(10), r.IsDBNull(11)?null:r.GetString(11), r.IsDBNull(12)?null:r.GetString(12), r.GetDateTime(13), r.IsDBNull(14)?null:r.GetString(14), r.IsDBNull(15)?null:r.GetString(15))),

            BraintreeTxns = await ReadAsync(cn, ct, "SELECT TransactionId, PaymentId, TransactionStatus, Amount, IsMismatch, IsVerified, IsFraudDetected, DateCreated FROM dbo.BRAINTREE_TRANSACTIONS",
                r => new BraintreeTxn(r.GetString(0), r.IsDBNull(1)?null:r.GetInt32(1), r.GetString(2), r.IsDBNull(3)?null:r.GetDecimal(3), r.GetBoolean(4), r.GetBoolean(5), r.GetBoolean(6), r.GetDateTime(7))),

            PaymentDetails = await ReadAsync(cn, ct, "SELECT Id, PaymentId, PackageId, CourseId, DiscountCode, BaseAmount, DiscountAmount, FinalAmount FROM dbo.USER_PAYMENT_DETAILS",
                r => new PaymentLineDetail(r.GetInt32(0), r.GetInt32(1), r.GetInt16(2), r.IsDBNull(3)?null:r.GetInt32(3), r.IsDBNull(4)?null:r.GetString(4), r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7))),

            ReferralCodes = await ReadAsync(cn, ct, "SELECT ReferralId, ReferralCode, ReferralCodeLimit, ReferralCodeUsed, CodeExpirationDate, DiscountGroupId, DiscountBehaviourTypeId, DiscountPercent FROM dbo.NCLEX_REFERRAL_CODES",
                r => new ReferralCode(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetDateTime(4), r.IsDBNull(5)?null:r.GetInt32(5), r.GetByte(6), r.GetDecimal(7))),

            ReferralUsages = await ReadAsync(cn, ct, "SELECT Id, ReferralCode, UserId, PaymentId, DateUsed FROM dbo.REFERRAL_CODE_USAGE",
                r => new ReferralUsage(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3)?null:r.GetInt32(3), r.GetDateTime(4))),

            Subscriptions = await ReadAsync(cn, ct, "SELECT SubscriptionId, UserId, CourseId, PackageId, Active, DateActivated, DateExpiration, DateFirstAccessed FROM dbo.USER_SUBSCRIPTIONS",
                r => new SubscriptionRecord(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt16(3), r.GetBoolean(4), r.GetDateTime(5), r.GetDateTime(6), r.IsDBNull(7)?null:r.GetDateTime(7))),

            UserLogs     = await ReadAsync(cn, ct, "SELECT Id, UserId, SubscriptionId, IPAddress, CountryCode, DeviceTypeId, LogType, Url, DateCreated FROM dbo.USER_LOGS",
                r => new UserLog(r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2)?null:r.GetInt32(2), r.GetString(3), r.IsDBNull(4)?null:r.GetString(4), r.IsDBNull(5)?null:r.GetByte(5), r.IsDBNull(6)?null:r.GetString(6), r.IsDBNull(7)?null:r.GetString(7), r.GetDateTime(8))),

            BlockedIps   = await ReadAsync(cn, ct, "SELECT Id, IPAddress, Reason FROM dbo.BLOCKED_IPS",
                r => new BlockedIp(r.GetInt32(0), r.GetString(1), r.IsDBNull(2)?null:r.GetString(2))),

            Countries    = await ReadAsync(cn, ct, "SELECT CountryId, Name, IsoCode, IsEmbargoed FROM dbo.COUNTRIES",
                r => new Country(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetBoolean(3))),

            GaDiscounts  = await ReadAsync(cn, ct, "SELECT DiscountId, DiscountGroupId, PackageId, DiscountAmount, IsPercent, DiscountBehaviourTypeId, FloorValue FROM dbo.GA_DISCOUNTS",
                r => new GaDiscount(r.GetInt32(0), r.GetInt32(1), r.GetInt16(2), r.GetDecimal(3), r.GetBoolean(4), r.GetByte(5), r.IsDBNull(6)?null:r.GetDecimal(6))),

            Users        = await ReadAsync(cn, ct, "SELECT UserId, Email, FirstName, LastName, CountryCode, RegistrationIp, DeviceFingerprint, UserAgent, SignupSource, DateCreated FROM dbo.USERS",
                r => new UserRecord(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4)?null:r.GetString(4), r.GetString(5), r.GetString(6), r.IsDBNull(7)?null:r.GetString(7), r.GetString(8), r.GetDateTime(9))),

            ApiLogs      = await ReadAsync(cn, ct, "SELECT Id, RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, UserAgent, DeviceFingerprint, DateCreated FROM dbo.API_LOGS WHERE DateCreated >= DATEADD(DAY, -30, SYSUTCDATETIME())",
                r => new ApiLogRecord(r.GetInt64(0), r.GetString(1), r.IsDBNull(2)?null:r.GetInt32(2), r.IsDBNull(3)?null:r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), r.GetString(7), r.GetString(8), r.IsDBNull(9)?null:r.GetString(9), r.IsDBNull(10)?null:r.GetString(10), r.IsDBNull(11)?null:r.GetString(11), r.GetDateTime(12))),

            Packages     = await ReadAsync(cn, ct, "SELECT PackageId, Name, ListPrice, DurationDays, TrialDays FROM dbo.PACKAGES",
                r => new PackageRecord(r.GetInt16(0), r.GetString(1), r.GetDecimal(2), r.GetInt32(3), r.GetInt32(4))),
        };
    }

    private static async Task<List<T>> ReadAsync<T>(SqlConnection cn, CancellationToken ct, string sql, Func<SqlDataReader, T> map)
    {
        var results = new List<T>();
        await using var cmd = new SqlCommand(sql, cn);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(map(reader));
        return results;
    }
}
