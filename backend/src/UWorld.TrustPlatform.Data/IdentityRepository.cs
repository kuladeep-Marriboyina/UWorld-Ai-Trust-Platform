using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Data;

public sealed record NewUser(string Email, string FirstName, string LastName, string? Phone, string? CountryCode,
    string Ip, string Device, string? UserAgent, string Password, string Source);

public sealed record ApiLogWrite(string RequestId, int? UserId, string? Email, string Method, string Endpoint, int StatusCode,
    string Outcome, string Ip, string? CountryCode, string? UserAgent, string? Device, int LatencyMs);

public sealed record SubscribeWrite(int UserId, short PackageId, bool Trial, string? DiscountCode, string? CardNumber, string Ip, string? CountryCode, string? Device);

public sealed record SubscribeResult(int SubscriptionId, int PaymentId, string Package, decimal ListPrice, decimal Discount, decimal Charged, string? CardLast4, DateTime Expires, string? CodeApplied, string? CodeRejectedReason);

public sealed record ProfileSubscription(int SubscriptionId, string Package, bool Active, DateTime Activated, DateTime Expires, decimal ListPrice);
public sealed record ProfilePayment(int PaymentId, decimal Amount, decimal Discount, string? CardLast4, string? CardFingerprint, string Kind, DateTime Created);

public sealed record AccountProfile(int UserId, string Email, string FirstName, string LastName, string? CountryCode,
    string RegistrationIp, string DeviceFingerprint, string? UserAgent, string SignupSource, DateTime Created,
    IReadOnlyList<ProfileSubscription> Subscriptions, IReadOnlyList<ProfilePayment> Payments,
    int ApiCalls, int FailedLogins, DateTime? LastSeenUtc);

public sealed record EntityReputation(string Type, string Value, int Accounts, int ApiCalls, int FailedLogins,
    DateTime? FirstSeenUtc, DateTime? LastSeenUtc, IReadOnlyList<string> Countries, bool Blocked, string? BlockedReason,
    int OtherCases, string Verdict);

/// <summary>A request for protected QBank content through one of the delivery channels.</summary>
public sealed record ContentAccessWrite(int UserId, short PackageId, int QuestionId, string Channel, string Ip, string Device, string RequestId);

public sealed record ContentAccessResult(bool Entitled, string AuthResult, long BytesReturned, string Resource, string Channel, string CorrelationId, string? SubscriptionState);

public sealed record PackageInfo(short PackageId, string Name, decimal ListPrice, int DurationDays, int TrialDays);

public interface IIdentityRepository
{
    Task<IReadOnlyList<PackageInfo>> GetPackagesAsync(CancellationToken ct = default);
    Task<int?> CreateUserAsync(NewUser u, CancellationToken ct = default);
    Task<(int UserId, string Email, string? PasswordHash)?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task LogApiAsync(ApiLogWrite w, CancellationToken ct = default);
    Task<SubscribeResult> SubscribeAsync(SubscribeWrite w, CancellationToken ct = default);
    Task<ContentAccessResult> AccessContentAsync(ContentAccessWrite w, CancellationToken ct = default);
    Task<IReadOnlyList<AccountProfile>> GetProfilesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);
    Task<IReadOnlyList<ApiLogRecord>> GetActivityAsync(IReadOnlyCollection<int> userIds, IReadOnlyCollection<string> ips, int take, CancellationToken ct = default);
    Task<EntityReputation> GetReputationAsync(string type, string value, int excludeCaseId, CancellationToken ct = default);
    static string Hash(string password) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("uw-demo:" + password)));
}

public sealed class IdentityRepository : IIdentityRepository
{
    private static readonly SemaphoreSlim IdGate = new(1, 1);
    private readonly ISqlConnectionFactory _factory;
    public IdentityRepository(ISqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<PackageInfo>> GetPackagesAsync(CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT PackageId, Name, ListPrice, DurationDays, TrialDays FROM dbo.PACKAGES ORDER BY PackageId", cn);
        var list = new List<PackageInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new PackageInfo(r.GetInt16(0), r.GetString(1), r.GetDecimal(2), r.GetInt32(3), r.GetInt32(4)));
        return list;
    }

    public async Task<int?> CreateUserAsync(NewUser u, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
IF EXISTS (SELECT 1 FROM dbo.USERS WHERE Email = @email) SELECT CAST(NULL AS INT)
ELSE INSERT dbo.USERS (Email, FirstName, LastName, Phone, CountryCode, RegistrationIp, DeviceFingerprint, UserAgent, PasswordHash, SignupSource, DateCreated)
     OUTPUT INSERTED.UserId
     VALUES (@email, @fn, @ln, @ph, @cc, @ip, @dev, @ua, @pw, @src, SYSUTCDATETIME())", cn);
        cmd.Parameters.AddWithValue("@email", u.Email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@fn", u.FirstName.Trim());
        cmd.Parameters.AddWithValue("@ln", u.LastName.Trim());
        cmd.Parameters.AddWithValue("@ph", (object?)u.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cc", (object?)u.CountryCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ip", u.Ip);
        cmd.Parameters.AddWithValue("@dev", u.Device);
        cmd.Parameters.AddWithValue("@ua", (object?)Trim(u.UserAgent, 256) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pw", IIdentityRepository.Hash(u.Password));
        cmd.Parameters.AddWithValue("@src", u.Source);
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return v is int id ? id : null;
    }

    public async Task<(int UserId, string Email, string? PasswordHash)?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT UserId, Email, PasswordHash FROM dbo.USERS WHERE Email = @e", cn);
        cmd.Parameters.AddWithValue("@e", email.Trim().ToLowerInvariant());
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (r.GetInt32(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2));
    }

    public async Task LogApiAsync(ApiLogWrite w, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"INSERT dbo.API_LOGS (RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, UserAgent, DeviceFingerprint, LatencyMs, DateCreated)
            VALUES (@r, @u, @e, @m, @ep, @sc, @o, @ip, @cc, @ua, @dev, @lat, SYSUTCDATETIME())", cn);
        cmd.Parameters.AddWithValue("@r", w.RequestId);
        cmd.Parameters.AddWithValue("@u", (object?)w.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", (object?)w.Email?.Trim().ToLowerInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@m", w.Method);
        cmd.Parameters.AddWithValue("@ep", w.Endpoint);
        cmd.Parameters.AddWithValue("@sc", w.StatusCode);
        cmd.Parameters.AddWithValue("@o", w.Outcome);
        cmd.Parameters.AddWithValue("@ip", w.Ip);
        cmd.Parameters.AddWithValue("@cc", (object?)w.CountryCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ua", (object?)Trim(w.UserAgent, 256) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dev", (object?)w.Device ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lat", w.LatencyMs);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeWrite w, CancellationToken ct = default)
    {
        await IdGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
            string name; decimal price; int duration, trialDays;
            await using (var cmd = new SqlCommand("SELECT Name, ListPrice, DurationDays, TrialDays FROM dbo.PACKAGES WHERE PackageId=@p", cn))
            {
                cmd.Parameters.AddWithValue("@p", w.PackageId);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await r.ReadAsync(ct).ConfigureAwait(false)) throw new ArgumentException("Unknown package.");
                name = r.GetString(0); price = r.GetDecimal(1); duration = r.GetInt32(2); trialDays = r.GetInt32(3);
            }
            if (w.Trial && trialDays == 0) throw new ArgumentException($"{name} has no free trial.");

            // discount code validation mirrors applydiscountcode: exists, not expired, under limit
            decimal discount = 0m; string? applied = null; string? rejected = null; int? referralId = null;
            if (!w.Trial && !string.IsNullOrWhiteSpace(w.DiscountCode))
            {
                await using var cmd = new SqlCommand("SELECT ReferralId, DiscountPercent, ReferralCodeLimit, ReferralCodeUsed, CodeExpirationDate FROM dbo.NCLEX_REFERRAL_CODES WHERE ReferralCode=@c", cn);
                cmd.Parameters.AddWithValue("@c", w.DiscountCode.Trim());
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await r.ReadAsync(ct).ConfigureAwait(false)) rejected = "unknown code";
                else if (r.GetDateTime(4) < DateTime.UtcNow) rejected = "code expired";
                else if (r.GetInt32(3) >= r.GetInt32(2)) rejected = "code usage limit reached";
                else { referralId = r.GetInt32(0); discount = Math.Round(price * r.GetDecimal(1) / 100m, 2); applied = w.DiscountCode.Trim().ToUpperInvariant(); }
            }

            var charged = w.Trial ? 0m : price - discount;
            string? last4 = null, fp = null;
            if (!w.Trial && !string.IsNullOrWhiteSpace(w.CardNumber))
            {
                var digits = new string(w.CardNumber.Where(char.IsDigit).ToArray());
                if (digits.Length < 12) throw new ArgumentException("Card number looks invalid.");
                last4 = digits[^4..];
                fp = "card-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digits)))[..10].ToLowerInvariant();
            }
            else if (!w.Trial) throw new ArgumentException("A card is required for a paid subscription.");

            int subId, payId;
            await using (var cmd = new SqlCommand("SELECT ISNULL(MAX(SubscriptionId), 6000) + 1 FROM dbo.USER_SUBSCRIPTIONS; SELECT ISNULL(MAX(PaymentId), 6000) + 1 FROM dbo.USER_PAYMENTS;", cn))
            {
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await r.ReadAsync(ct).ConfigureAwait(false); subId = r.GetInt32(0);
                await r.NextResultAsync(ct).ConfigureAwait(false); await r.ReadAsync(ct).ConfigureAwait(false); payId = r.GetInt32(0);
            }
            var expires = DateTime.UtcNow.AddDays(w.Trial ? trialDays : duration);
            var txn = w.Trial ? null : "bt_" + Guid.NewGuid().ToString("N")[..10];

            await using (var cmd = new SqlCommand(@"
INSERT dbo.USER_SUBSCRIPTIONS (SubscriptionId, UserId, CourseId, PackageId, StatusId, Active, DateActivated, DateExpiration, DateFirstAccessed)
VALUES (@sub, @u, 1, @pkg, 1, 1, SYSUTCDATETIME(), @exp, NULL);
INSERT dbo.USER_PAYMENTS (PaymentId, UserId, FinalAmount, TransactionAmount, DiscountAmount, TransactionId, PaymentStatus, GatewayId, PaymentModeId,
    ResponseCode, SourceId, PaymentTypeId, IPAddress, CountryCode, DateCreated, DatePaymentReceived, CardFingerprint, CardLast4)
VALUES (@pay, @u, @charged, @charged, @disc, @txn, 1, @gw, @mode, 1, 2, @ptype, @ip, @cc, SYSUTCDATETIME(), SYSUTCDATETIME(), @fp, @l4);
INSERT dbo.USER_PAYMENT_DETAILS (PaymentId, PackageId, CourseId, DiscountCode, BaseAmount, DiscountAmount, FinalAmount)
VALUES (@pay, @pkg, 1, @code, @price, @disc, @charged);
IF @txn IS NOT NULL
    INSERT dbo.BRAINTREE_TRANSACTIONS (TransactionId, PaymentId, TransactionStatus, Amount, IsMismatch, IsVerified, IsFraudDetected, DateCreated)
    VALUES (@txn, @pay, 'settled', @charged, 0, 1, 0, SYSUTCDATETIME());
INSERT dbo.USER_LOGS (UserId, SubscriptionId, IPAddress, CountryCode, DeviceTypeId, LogType, Url, DateCreated)
VALUES (@u, @sub, @ip, @cc, 1, 'Access', '/api/qbank/package/' + CAST(@pkg AS NVARCHAR(8)) + '/access', SYSUTCDATETIME());", cn))
            {
                cmd.Parameters.AddWithValue("@sub", subId);
                cmd.Parameters.AddWithValue("@pay", payId);
                cmd.Parameters.AddWithValue("@u", w.UserId);
                cmd.Parameters.AddWithValue("@pkg", w.PackageId);
                cmd.Parameters.AddWithValue("@exp", expires);
                cmd.Parameters.AddWithValue("@charged", charged);
                cmd.Parameters.AddWithValue("@disc", w.Trial ? price : discount);
                cmd.Parameters.AddWithValue("@price", price);
                cmd.Parameters.AddWithValue("@txn", (object?)txn ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@gw", w.Trial ? DBNull.Value : (byte)4);
                cmd.Parameters.AddWithValue("@mode", (byte)(w.Trial ? 6 : 1));
                cmd.Parameters.AddWithValue("@ptype", (byte)(w.Trial ? 7 : 1));
                cmd.Parameters.AddWithValue("@ip", w.Ip);
                cmd.Parameters.AddWithValue("@cc", (object?)w.CountryCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fp", (object?)fp ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@l4", (object?)last4 ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@code", (object?)applied ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (referralId is int rid)
            {
                await using var cmd = new SqlCommand(@"
UPDATE dbo.NCLEX_REFERRAL_CODES SET ReferralCodeUsed = ReferralCodeUsed + 1 WHERE ReferralId=@rid;
INSERT dbo.REFERRAL_CODE_USAGE (ReferralCode, UserId, PaymentId, DateUsed) VALUES (@code, @u, @pay, SYSUTCDATETIME());
INSERT dbo.USER_LOGS (UserId, SubscriptionId, IPAddress, CountryCode, DeviceTypeId, LogType, Url, DateCreated)
VALUES (@u, @sub, @ip, @cc, 1, 'ApplyDiscountCode', '/api/v2/orders/applydiscountcode?code=' + @code + '&packageId=' + CAST(@pkg AS NVARCHAR(8)), SYSUTCDATETIME());", cn);
                cmd.Parameters.AddWithValue("@rid", rid);
                cmd.Parameters.AddWithValue("@code", applied!);
                cmd.Parameters.AddWithValue("@u", w.UserId);
                cmd.Parameters.AddWithValue("@pay", payId);
                cmd.Parameters.AddWithValue("@sub", subId);
                cmd.Parameters.AddWithValue("@pkg", w.PackageId);
                cmd.Parameters.AddWithValue("@ip", w.Ip);
                cmd.Parameters.AddWithValue("@cc", (object?)w.CountryCode ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return new SubscribeResult(subId, payId, name, price, w.Trial ? price : discount, charged, last4, expires, applied, rejected);
        }
        finally { IdGate.Release(); }
    }

    /// <summary>
    /// Serves protected content the way the platform's three delivery paths behave:
    ///   app    — checks the subscription and returns nothing when denied (correct);
    ///   export — the bulk "Download PDF" endpoint records the deny but still streams the file (the bug being exploited);
    ///   cache  — a mobile/CDN cache that keeps serving after access was revoked.
    /// Every request writes an AuthOutcomes row and an AccessEvents row on one correlation id — exactly what the
    /// authorization-gap detector compares.
    /// </summary>
    public async Task<ContentAccessResult> AccessContentAsync(ContentAccessWrite w, CancellationToken ct = default)
    {
        await IdGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
            string? state = null; var entitled = false;
            await using (var cmd = new SqlCommand(@"SELECT TOP 1 Active, DateExpiration FROM dbo.USER_SUBSCRIPTIONS
                                                    WHERE UserId=@u AND PackageId=@p ORDER BY DateExpiration DESC", cn))
            {
                cmd.Parameters.AddWithValue("@u", w.UserId);
                cmd.Parameters.AddWithValue("@p", w.PackageId);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    entitled = r.GetBoolean(0) && r.GetDateTime(1) > DateTime.UtcNow;
                    state = entitled ? "active" : "expired";
                }
            }
            var channel = w.Channel is "export" or "cache" ? w.Channel : "app";
            var resource = channel switch
            {
                "export" => $"/qbank/package/{w.PackageId}/export.pdf",
                "cache" => $"/cdn/qbank/package/{w.PackageId}/q/{w.QuestionId}",
                _ => $"/qbank/package/{w.PackageId}/q/{w.QuestionId}"
            };
            var authResult = entitled ? "Allowed" : "Denied";
            long bytes = entitled ? (channel == "export" ? 2_400_000 : 48_000)
                       : channel == "export" ? 2_400_000          // vulnerable: ignores the deny
                       : channel == "cache" ? 52_000              // stale cache: never re-checks
                       : 0;                                       // app: correct
            var corr = "C-" + w.RequestId + "-" + Guid.NewGuid().ToString("N")[..6];

            await using (var cmd = new SqlCommand(@"
DECLARE @authId INT = (SELECT ISNULL(MAX(AuthId), 0) + 1 FROM dbo.AuthOutcomes);
INSERT dbo.AuthOutcomes (AuthId, AccountId, Resource, AuthResult, CorrelationId, OccurredUtc) VALUES (@authId, @u, @res, @auth, @corr, SYSUTCDATETIME());
DECLARE @accId INT = (SELECT ISNULL(MAX(AccessId), 0) + 1 FROM dbo.AccessEvents);
INSERT dbo.AccessEvents (AccessId, AccountId, Resource, PackageId, QuestionId, SessionId, ContentReturnedBytes, IpAddress, DeviceId, CorrelationId, OccurredUtc)
VALUES (@accId, @u, @res, @p, @q, @sess, @bytes, @ip, @dev, @corr, DATEADD(MILLISECOND, 5, SYSUTCDATETIME()));", cn))
            {
                cmd.Parameters.AddWithValue("@u", w.UserId);
                cmd.Parameters.AddWithValue("@res", resource);
                cmd.Parameters.AddWithValue("@auth", authResult);
                cmd.Parameters.AddWithValue("@corr", corr);
                cmd.Parameters.AddWithValue("@p", (int)w.PackageId);
                cmd.Parameters.AddWithValue("@q", w.QuestionId);
                cmd.Parameters.AddWithValue("@sess", "SF-" + w.UserId);
                cmd.Parameters.AddWithValue("@bytes", bytes);
                cmd.Parameters.AddWithValue("@ip", w.Ip);
                cmd.Parameters.AddWithValue("@dev", w.Device);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return new ContentAccessResult(entitled, authResult, bytes, resource, channel, corr, state);
        }
        finally { IdGate.Release(); }
    }

    public async Task<IReadOnlyList<AccountProfile>> GetProfilesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return Array.Empty<AccountProfile>();
        var idList = string.Join(",", userIds.Distinct().Take(200));   // ints only — safe to inline
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);

        var subs = new Dictionary<int, List<ProfileSubscription>>();
        await using (var cmd = new SqlCommand($@"SELECT s.UserId, s.SubscriptionId, COALESCE(p.Name, 'package ' + CAST(s.PackageId AS NVARCHAR(8))), s.Active, s.DateActivated, s.DateExpiration, COALESCE(p.ListPrice, 0)
              FROM dbo.USER_SUBSCRIPTIONS s LEFT JOIN dbo.PACKAGES p ON p.PackageId = s.PackageId WHERE s.UserId IN ({idList}) ORDER BY s.DateActivated", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!subs.TryGetValue(r.GetInt32(0), out var l)) subs[r.GetInt32(0)] = l = new();
                l.Add(new ProfileSubscription(r.GetInt32(1), r.GetString(2), r.GetBoolean(3), r.GetDateTime(4), r.GetDateTime(5), r.GetDecimal(6)));
            }
        }
        var pays = new Dictionary<int, List<ProfilePayment>>();
        await using (var cmd = new SqlCommand($@"SELECT UserId, PaymentId, FinalAmount, DiscountAmount, CardLast4, CardFingerprint, PaymentTypeId, DateCreated
              FROM dbo.USER_PAYMENTS WHERE UserId IN ({idList}) ORDER BY DateCreated", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                if (!pays.TryGetValue(r.GetInt32(0), out var l)) pays[r.GetInt32(0)] = l = new();
                var type = r.IsDBNull(6) ? (byte)1 : r.GetByte(6);
                l.Add(new ProfilePayment(r.GetInt32(1), r.GetDecimal(2), r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                    type == 7 ? "Trial / complimentary" : r.GetDecimal(3) > 0 ? "Discounted" : "Paid", r.GetDateTime(7)));
            }
        }
        var activity = new Dictionary<int, (int calls, int failed, DateTime? last)>();
        await using (var cmd = new SqlCommand($@"SELECT UserId, COUNT(*), SUM(CASE WHEN Outcome='Failed' THEN 1 ELSE 0 END), MAX(DateCreated)
              FROM dbo.API_LOGS WHERE UserId IN ({idList}) GROUP BY UserId", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                activity[r.GetInt32(0)] = (r.GetInt32(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetDateTime(3));
        }

        var list = new List<AccountProfile>();
        await using (var cmd = new SqlCommand($@"SELECT UserId, Email, FirstName, LastName, CountryCode, RegistrationIp, DeviceFingerprint, UserAgent, SignupSource, DateCreated
              FROM dbo.USERS WHERE UserId IN ({idList}) ORDER BY DateCreated", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = r.GetInt32(0);
                var a = activity.TryGetValue(id, out var x) ? x : (0, 0, (DateTime?)null);
                list.Add(new AccountProfile(id, r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetString(8), r.GetDateTime(9),
                    subs.TryGetValue(id, out var s) ? s : new(), pays.TryGetValue(id, out var p) ? p : new(), a.Item1, a.Item2, a.Item3));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<ApiLogRecord>> GetActivityAsync(IReadOnlyCollection<int> userIds, IReadOnlyCollection<string> ips, int take, CancellationToken ct = default)
    {
        if (userIds.Count == 0 && ips.Count == 0) return Array.Empty<ApiLogRecord>();
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand();
        cmd.Connection = cn;
        var ipParams = ips.Take(20).Select((ip, i) => { cmd.Parameters.AddWithValue($"@ip{i}", ip); return $"@ip{i}"; }).ToList();
        var userClause = userIds.Count > 0 ? $"UserId IN ({string.Join(",", userIds.Distinct().Take(200))})" : "1=0";
        var ipClause = ipParams.Count > 0 ? $"IPAddress IN ({string.Join(",", ipParams)})" : "1=0";
        cmd.CommandText = $@"SELECT TOP (@take) Id, RequestId, UserId, Email, Method, Endpoint, StatusCode, Outcome, IPAddress, CountryCode, UserAgent, DeviceFingerprint, DateCreated
            FROM dbo.API_LOGS WHERE ({userClause} OR {ipClause}) ORDER BY DateCreated DESC";
        cmd.Parameters.AddWithValue("@take", take);
        var list = new List<ApiLogRecord>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            list.Add(new ApiLogRecord(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetInt32(6), r.GetString(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11), r.GetDateTime(12)));
        return list;
    }

    public async Task<EntityReputation> GetReputationAsync(string type, string value, int excludeCaseId, CancellationToken ct = default)
    {
        await using var cn = await _factory.OpenAsync(ct).ConfigureAwait(false);
        int accounts = 0, calls = 0, failed = 0, others = 0; DateTime? first = null, last = null; var countries = new List<string>();
        bool blocked = false; string? reason = null;

        string? accountsSql = type switch
        {
            "ip" => "SELECT COUNT(*), MIN(DateCreated), MAX(DateCreated) FROM dbo.USERS WHERE RegistrationIp=@v",
            "device" => "SELECT COUNT(*), MIN(DateCreated), MAX(DateCreated) FROM dbo.USERS WHERE DeviceFingerprint=@v",
            "card" => "SELECT COUNT(DISTINCT UserId), MIN(DateCreated), MAX(DateCreated) FROM dbo.USER_PAYMENTS WHERE CardFingerprint=@v",
            "domain" => "SELECT COUNT(*), MIN(DateCreated), MAX(DateCreated) FROM dbo.USERS WHERE Email LIKE '%@' + @v",
            _ => null
        };
        if (accountsSql != null)
        {
            await using var cmd = new SqlCommand(accountsSql, cn);
            cmd.Parameters.AddWithValue("@v", value);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false)) { accounts = r.GetInt32(0); first = r.IsDBNull(1) ? null : r.GetDateTime(1); last = r.IsDBNull(2) ? null : r.GetDateTime(2); }
        }
        string? apiSql = type switch
        {
            "ip" => "SELECT COUNT(*), SUM(CASE WHEN Outcome='Failed' THEN 1 ELSE 0 END), MIN(DateCreated), MAX(DateCreated) FROM dbo.API_LOGS WHERE IPAddress=@v",
            "device" => "SELECT COUNT(*), SUM(CASE WHEN Outcome='Failed' THEN 1 ELSE 0 END), MIN(DateCreated), MAX(DateCreated) FROM dbo.API_LOGS WHERE DeviceFingerprint=@v",
            "email" => "SELECT COUNT(*), SUM(CASE WHEN Outcome='Failed' THEN 1 ELSE 0 END), MIN(DateCreated), MAX(DateCreated) FROM dbo.API_LOGS WHERE Email=@v",
            "user" => "SELECT COUNT(*), SUM(CASE WHEN Outcome='Failed' THEN 1 ELSE 0 END), MIN(DateCreated), MAX(DateCreated) FROM dbo.API_LOGS WHERE UserId=TRY_CAST(@v AS INT)",
            _ => null
        };
        if (apiSql != null)
        {
            await using var cmd = new SqlCommand(apiSql, cn);
            cmd.Parameters.AddWithValue("@v", value);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                calls = r.GetInt32(0); failed = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                if (!r.IsDBNull(2) && (first is null || r.GetDateTime(2) < first)) first = r.GetDateTime(2);
                if (!r.IsDBNull(3) && (last is null || r.GetDateTime(3) > last)) last = r.GetDateTime(3);
            }
        }
        if (type == "ip")
        {
            await using (var cmd = new SqlCommand("SELECT DISTINCT TOP 6 CountryCode FROM dbo.API_LOGS WHERE IPAddress=@v AND CountryCode IS NOT NULL", cn))
            {
                cmd.Parameters.AddWithValue("@v", value);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false)) countries.Add(r.GetString(0));
            }
            await using (var cmd = new SqlCommand("SELECT TOP 1 Reason FROM dbo.BLOCKED_IPS WHERE IPAddress=@v", cn))
            {
                cmd.Parameters.AddWithValue("@v", value);
                var res = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (res != null) { blocked = true; reason = res as string ?? "blocked"; }
            }
        }
        await using (var cmd = new SqlCommand("SELECT COUNT(DISTINCT CaseId) FROM dbo.CaseEntities WHERE EntityType=@t AND EntityValue=@v AND CaseId <> @c", cn))
        {
            cmd.Parameters.AddWithValue("@t", type);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.Parameters.AddWithValue("@c", excludeCaseId);
            others = (int)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        }

        var verdict = blocked ? $"On the block list ({reason})."
            : others > 0 ? $"Seen in {others} other case(s) — repeat offender."
            : type == "domain" && IsDisposableDomain(value) ? "Disposable email provider."
            : accounts >= 5 ? $"Linked to {accounts} accounts — far above a normal household (1–2)."
            : accounts >= 2 ? $"Linked to {accounts} accounts."
            : failed >= 5 ? $"{failed} failed logins recorded."
            : "No prior history.";
        return new EntityReputation(type, value, accounts, calls, failed, first, last, countries, blocked, reason, others, verdict);
    }

    private static bool IsDisposableDomain(string d) =>
        d is "mailinator.com" or "guerrillamail.com" or "10minutemail.com" or "tempmail.com" or "yopmail.com" or "trashmail.com" or "sharklasers.com" or "maildrop.cc";

    private static string? Trim(string? s, int n) => s is null ? null : s.Length > n ? s[..n] : s;
}
