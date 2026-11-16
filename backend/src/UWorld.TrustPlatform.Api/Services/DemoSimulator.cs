using Microsoft.Data.SqlClient;
using UWorld.TrustPlatform.Data;
using UWorld.TrustPlatform.Ingestion;

namespace UWorld.TrustPlatform.Api.Services;

public sealed record SimulatedCase(int CaseId, string RuleId, string Severity, string Title);

public sealed record SimulationResult(string Scenario, string CorrelationId, int CasesCreated,
    IReadOnlyList<string> Injected, IReadOnlyList<SimulatedCase> NewCases);

/// <summary>One bypass technique the demo can inject live.</summary>
public sealed record ScenarioVariant(string Key, string Label, string Technique, string Tables, string Detail, string Expect);

/// <summary>An attack family (high-level menu entry) with several bypass variants.</summary>
public sealed record ScenarioGroup(string Key, string Label, string Icon, string Summary, string RuleId, IReadOnlyList<ScenarioVariant> Variants);

/// <summary>
/// Injects a fresh anomaly into the source data and runs one ingestion pass, so a real
/// RiskCase appears live on stage during the demo. Every attack family has several bypass
/// variants so the demo shows the rules catch the technique, not just one planted row.
/// </summary>
public sealed class DemoSimulator
{
    private readonly ISqlConnectionFactory _factory;
    private readonly IngestionWorker _worker;

    public DemoSimulator(ISqlConnectionFactory factory, IngestionWorker worker)
    {
        _factory = factory;
        _worker = worker;
    }

    public static readonly IReadOnlyList<ScenarioGroup> Catalog = new[]
    {
        new ScenarioGroup("payment", "Paid without Braintree", "💳",
            "Orders that show as Paid even though the money never reached (or never matched) the Braintree gateway.",
            "paid-without-braintree", new[]
            {
                new ScenarioVariant("pwb-placeholder-zero", "Placeholder transaction id \"0\"", "Zero-amount code path reused for a real order",
                    "USER_PAYMENTS + USER_PAYMENT_DETAILS",
                    "A $250 card order is written with PaymentStatus=Paid, GatewayId=4 (Braintree) and TransactionId \"0\" — the placeholder the zero-amount/store-credit paths use. No BRAINTREE_TRANSACTIONS row exists.",
                    "Critical paid-without-braintree case: \"no gateway transaction id (placeholder)\"."),
                new ScenarioVariant("pwb-null-txn", "Missing transaction id (NULL)", "Checkout marked Paid before the gateway call returned",
                    "USER_PAYMENTS + USER_PAYMENT_DETAILS",
                    "A $399 order is marked Paid with TransactionId NULL — what happens when the order is committed and the Braintree call then times out or throws.",
                    "Critical case: placeholder / empty gateway transaction id."),
                new ScenarioVariant("pwb-forged-txn", "Forged transaction id", "Client-supplied txn id that Braintree never issued",
                    "USER_PAYMENTS + USER_PAYMENT_DETAILS",
                    "A $299 order carries a realistic-looking id (bt_xxxxxx) that the attacker supplied in the request. It looks legitimate, but there is no matching row in BRAINTREE_TRANSACTIONS.",
                    "Critical case: \"no matching Braintree transaction row\"."),
                new ScenarioVariant("pwb-amount-tamper", "Amount tampering ($9.99 charged for $299)", "Real gateway txn, but for a tampered amount",
                    "USER_PAYMENTS + BRAINTREE_TRANSACTIONS",
                    "Braintree really settled a transaction — but for $9.99, while the order records $299 as paid. The reconciliation job set IsMismatch=1.",
                    "Critical case: \"Braintree transaction flagged IsMismatch\"."),
                new ScenarioVariant("pwb-offline-path", "Offline/support path on the card gateway", "Check/support-enrollment flow abused to mark a card order Paid",
                    "USER_PAYMENTS + USER_PAYMENT_DETAILS",
                    "A $499 order uses PaymentModeId=2 (Check) and SourceId=4 (SupportEnrollment) yet is booked on GatewayId=4 with TransactionId \"0000\" — an offline-payment shortcut abused for a card order.",
                    "Critical case: placeholder transaction id \"0000\"."),
            }),

        new ScenarioGroup("region", "Same subscription, many regions", "🌍",
            "One subscription used from several countries in a short time — account sharing, credential resale or VPN hopping.",
            "same-subscription-region", new[]
            {
                new ScenarioVariant("reg-us-in", "US → India in 40 minutes", "Impossible travel",
                    "USER_SUBSCRIPTIONS + USER_LOGS",
                    "A new subscription is accessed from the US (72.14.x) and then from India (49.36.x) 40 minutes later — physically impossible for one person.",
                    "High same-subscription-region case listing US, IN."),
                new ScenarioVariant("reg-3-country-ring", "Sharing ring across 3 countries", "Credentials resold / shared in a group",
                    "USER_SUBSCRIPTIONS + USER_LOGS",
                    "One subscription is used from the US, the UK and Canada within 3 hours from three different devices — the pattern of credentials shared in a study group or sold online.",
                    "High case listing 3 countries (US, GB, CA)."),
                new ScenarioVariant("reg-vpn-flip", "VPN flip-flopping (US ↔ GB)", "Proxy/VPN hopping to dodge geo checks",
                    "USER_SUBSCRIPTIONS + USER_LOGS",
                    "Same device alternates US → GB → US → GB within 12 minutes — exit-node hopping that a single-IP rule would never notice.",
                    "High case listing US, GB."),
                new ScenarioVariant("reg-b2b-seat-leak", "B2B seat used overseas", "Institutional seat credentials leaked",
                    "USER_SUBSCRIPTIONS + USER_LOGS",
                    "A university-issued seat is used on campus in the US, then two hours later from India on a different device — the seat's credentials have left the institution.",
                    "High case listing US, IN."),
            }),

        new ScenarioGroup("discount", "Discount code abuse", "🏷️",
            "Discount and referral codes used in ways applydiscountcode should never allow.",
            "discount-code-fraud", new[]
            {
                new ScenarioVariant("disc-over-limit", "Code redeemed over its limit", "Usage counter bypassed",
                    "NCLEX_REFERRAL_CODES",
                    "A new 40%-off referral code shows ReferralCodeUsed = 77 against a ReferralCodeLimit of 50 — 27 redemptions the policy never allowed.",
                    "High discount-code-fraud [OverLimit] case."),
                new ScenarioVariant("disc-expired", "Expired code still applied", "Expiry check skipped (cached cart / replayed request)",
                    "USER_PAYMENTS + REFERRAL_CODE_USAGE + USER_LOGS",
                    "EXPIRED20 (expired 5 days ago) is applied to a new order — e.g. a saved cart or a replayed applydiscountcode request that skipped the expiry check.",
                    "High [Expired] case with the applydiscountcode URL as evidence."),
                new ScenarioVariant("disc-reuse", "Single-use code used twice by one user", "Same user re-applies a one-time code",
                    "NCLEX_REFERRAL_CODES + REFERRAL_CODE_USAGE",
                    "A new single-use (Regular) code is redeemed twice by the same user on two different orders, 10 minutes apart.",
                    "High [PerUserReuse] case."),
                new ScenarioVariant("disc-race", "Race condition double-use", "Two checkouts hit the non-atomic counter together",
                    "NCLEX_REFERRAL_CODES + REFERRAL_CODE_USAGE",
                    "Two different users redeem the same code 2 seconds apart — both read ReferralCodeUsed before either increment lands, so the limit is bypassed.",
                    "High [Race] case."),
                new ScenarioVariant("disc-stacking", "Stacking two non-stackable codes", "SAVE50 + FRIEND15 combined on one order",
                    "USER_PAYMENTS + USER_PAYMENT_DETAILS",
                    "One order carries both SAVE50 and FRIEND15 — both are Regular (non-stackable) codes, so they can never legitimately be combined.",
                    "High [Stacking] case."),
                new ScenarioVariant("disc-package-mismatch", "Code applied to the wrong package", "Package-eligibility check bypassed",
                    "USER_PAYMENTS + USER_PAYMENT_DETAILS + USER_LOGS",
                    "SAVE50 (valid only for packages 10, 11) is applied to package 12 by tampering with packageId in the applydiscountcode call.",
                    "High [PackageMismatch] case with eligible set [10, 11] and the URL used."),
            }),

        new ScenarioGroup("access", "Blocked IP / embargo", "🚫",
            "Traffic that should never reach the platform: blocked IPs and embargoed countries.",
            "blocked-ip-embargo", new[]
            {
                new ScenarioVariant("blk-scraper", "Known scraper IP", "Blocked content-scraping source got through",
                    "USER_LOGS (vs BLOCKED_IPS)",
                    "An access log from 45.66.77.88 — listed in BLOCKED_IPS as a known content-scraping source.",
                    "High blocked-ip-embargo case."),
                new ScenarioVariant("blk-chargeback", "Chargeback-fraud IP logs in", "Blocked payment-fraud source",
                    "USER_LOGS (vs BLOCKED_IPS)",
                    "A login from 103.21.44.9 — blocked for repeat chargeback fraud.",
                    "High blocked-ip-embargo case."),
                new ScenarioVariant("emb-iran", "Access from an embargoed country (IR)", "Geo-embargo bypassed",
                    "USER_LOGS (vs COUNTRIES)",
                    "An access log geo-located to Iran (IsEmbargoed=1).",
                    "Medium embargo case — plus a High same-subscription-region case, because this subscription was just used from the US."),
                new ScenarioVariant("emb-north-korea", "Access from an embargoed country (KP)", "Geo-embargo bypassed",
                    "USER_LOGS (vs COUNTRIES)",
                    "An access log geo-located to North Korea (IsEmbargoed=1).",
                    "Medium embargo case — plus a High same-subscription-region case, because this subscription was just used from the US."),
                new ScenarioVariant("blk-combo", "Blocked IP inside an embargoed country", "Two controls failing at once",
                    "USER_LOGS",
                    "One request from blocked IP 45.66.77.88 that also geo-locates to Iran — both the IP block and the embargo should have stopped it.",
                    "Blocked-IP (High) + embargo (Medium) — and a region-hop case for the subscription (US → IR)."),
            }),

        new ScenarioGroup("authz", "Authorization gap", "🔓",
            "Authorization said no (or never answered), but protected content was returned anyway.",
            "authorization-gap", new[]
            {
                new ScenarioVariant("gap-denied", "Denied, content returned", "Controller ignored the auth result",
                    "AuthOutcomes + AccessEvents",
                    "A Denied authorization plus an access event that still returned 61,000 bytes on the same correlation id.",
                    "Critical authorization-gap case."),
                new ScenarioVariant("gap-missing", "Auth service timed out (fail-open)", "No decision recorded, content served",
                    "AuthOutcomes + AccessEvents",
                    "The authorization call timed out and recorded AuthResult=Missing, but the endpoint failed open and served 48,000 bytes.",
                    "Critical case: \"after Missing authorization\" — plus a 'used after subscription ended' case (the account has no active subscription)."),
                new ScenarioVariant("gap-export", "Bulk PDF export after denial", "Export endpoint skips the subscription check",
                    "AuthOutcomes + AccessEvents",
                    "The question view was Denied, but the /export.pdf endpoint for the same package returned 2.4 MB — an entire question set.",
                    "Critical case with a large byte count."),
                new ScenarioVariant("gap-replay", "Replayed token — 3 requests", "One denied token replayed repeatedly",
                    "AuthOutcomes + AccessEvents",
                    "A single Denied authorization is followed by 3 requests replaying the same correlation/token, each returning content.",
                    "Three Critical cases, one per leaked response (plus a 'used after subscription ended' case for each)."),
                new ScenarioVariant("gap-stale-cache", "Refunded user served from cache", "CDN/cache serves content after access was revoked",
                    "AuthOutcomes + AccessEvents",
                    "After a refund the user is correctly Denied, but a stale cache layer still served 18,000 bytes of package 10.",
                    "Critical case."),
            }),
    };

    /// <summary>Legacy keys from the first simulate menu still work.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["paid-without-braintree"] = "pwb-placeholder-zero",
        ["region-hop"] = "reg-us-in",
        ["code-over-limit"] = "disc-over-limit",
        ["blocked-ip"] = "blk-scraper",
        ["authorization-gap"] = "gap-denied",
    };

    public Task<SimulationResult> SimulateAuthorizationGapAsync(string requestId, CancellationToken ct)
        => SimulateScenarioAsync("gap-denied", requestId, ct);

    public async Task<SimulationResult> SimulateScenarioAsync(string scenario, string requestId, CancellationToken ct)
    {
        var key = Aliases.TryGetValue(scenario, out var a) ? a : scenario;
        if (!Catalog.SelectMany(g => g.Variants).Any(v => v.Key == key))
            throw new ArgumentException($"Unknown scenario '{scenario}'.");

        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var injected = new List<string>();
        await using var cn = await _factory.OpenAsync(ct);
        var beforeCaseId = await ScalarIntAsync(cn, "SELECT ISNULL(MAX(CaseId), 0) FROM dbo.RiskCases", ct);

        switch (key)
        {
            // ---- payment integrity ----
            case "pwb-placeholder-zero":
                await PaymentAsync(cn, ct, injected, 103, 250m, "0", pkg: 11);
                break;
            case "pwb-null-txn":
                await PaymentAsync(cn, ct, injected, 105, 399m, null, pkg: 10);
                break;
            case "pwb-forged-txn":
                await PaymentAsync(cn, ct, injected, 107, 299m, "bt_" + tag.ToLowerInvariant(), pkg: 12);
                break;
            case "pwb-amount-tamper":
            {
                var txn = "bt_" + tag.ToLowerInvariant();
                var pid = await PaymentAsync(cn, ct, injected, 109, 299m, txn, pkg: 11);
                await Exec(cn, ct,
                    @"INSERT dbo.BRAINTREE_TRANSACTIONS (TransactionId,PaymentId,TransactionStatus,Amount,IsMismatch,IsVerified,IsFraudDetected,DateCreated)
                      VALUES (@t, @p, 'settled', 9.99, 1, 0, 0, SYSUTCDATETIME())", ("@t", txn), ("@p", pid));
                injected.Add($"BRAINTREE_TRANSACTIONS {txn}: settled $9.99, IsMismatch=1 (order says $299)");
                break;
            }
            case "pwb-offline-path":
                await PaymentAsync(cn, ct, injected, 111, 499m, "0000", pkg: 10, modeId: 2, sourceId: 4);
                break;

            // ---- region ----
            case "reg-us-in":
                await RegionAsync(cn, ct, injected, 120, new[] { ("US", "72.14.9.9", 1, -40), ("IN", "49.36.9.9", 2, 0) });
                break;
            case "reg-3-country-ring":
                await RegionAsync(cn, ct, injected, 121, new[] { ("US", "72.14.3.3", 1, -170), ("GB", "81.2.69.160", 2, -95), ("CA", "24.48.0.1", 3, -10) });
                break;
            case "reg-vpn-flip":
                await RegionAsync(cn, ct, injected, 122, new[] { ("US", "104.28.1.1", 1, -12), ("GB", "185.159.1.1", 1, -8), ("US", "104.28.1.2", 1, -4), ("GB", "185.159.1.2", 1, 0) });
                break;
            case "reg-b2b-seat-leak":
                await RegionAsync(cn, ct, injected, 123, new[] { ("US", "128.95.1.20", 1, -125), ("IN", "117.200.4.4", 2, 0) });
                break;

            // ---- discount ----
            case "disc-over-limit":
            {
                var code = "SIM" + tag;
                await CodeAsync(cn, ct, injected, code, limit: 50, used: 77, group: 10, percent: 40);
                break;
            }
            case "disc-expired":
            {
                var pid = await PaymentAsync(cn, ct, injected, 104, 160m, "bt_ok_" + tag.ToLowerInvariant(), pkg: 10, code: "EXPIRED20", discount: 40m, settle: true);
                await UsageAsync(cn, ct, injected, "EXPIRED20", 104, pid, 0);
                await ApplyUrlLogAsync(cn, ct, injected, 104, 5004, "EXPIRED20", 10);
                break;
            }
            case "disc-reuse":
            {
                var code = "ONCE" + tag;
                await CodeAsync(cn, ct, injected, code, limit: 500, used: 2, group: 13, percent: 25);
                var p1 = await PaymentAsync(cn, ct, injected, 108, 149m, "bt_a_" + tag.ToLowerInvariant(), pkg: 10, code: code, discount: 50m, settle: true);
                var p2 = await PaymentAsync(cn, ct, injected, 108, 149m, "bt_b_" + tag.ToLowerInvariant(), pkg: 10, code: code, discount: 50m, settle: true);
                await UsageAsync(cn, ct, injected, code, 108, p1, -600);
                await UsageAsync(cn, ct, injected, code, 108, p2, 0);
                break;
            }
            case "disc-race":
            {
                var code = "RACE" + tag;
                await CodeAsync(cn, ct, injected, code, limit: 1, used: 1, group: 13, percent: 30);
                await UsageAsync(cn, ct, injected, code, 110, null, -2);
                await UsageAsync(cn, ct, injected, code, 112, null, 0);
                break;
            }
            case "disc-stacking":
            {
                var pid = await PaymentAsync(cn, ct, injected, 102, 85m, "bt_s_" + tag.ToLowerInvariant(), pkg: 10, code: "SAVE50", discount: 100m, settle: true);
                await DetailAsync(cn, ct, injected, pid, 10, "FRIEND15", 0m, 15m);
                break;
            }
            case "disc-package-mismatch":
            {
                await PaymentAsync(cn, ct, injected, 110, 99.5m, "bt_m_" + tag.ToLowerInvariant(), pkg: 12, code: "SAVE50", discount: 99.5m, settle: true);
                await ApplyUrlLogAsync(cn, ct, injected, 110, 5010, "SAVE50", 12);
                break;
            }

            // ---- blocked / embargo ----
            case "blk-scraper":
                await LogAsync(cn, ct, injected, 120, 5003, "45.66.77.88", "US", "Access", "/api/qbank/package/11/questions");
                break;
            case "blk-chargeback":
                await LogAsync(cn, ct, injected, 106, 5006, "103.21.44.9", "US", "Login", "/api/account/login");
                break;
            case "emb-iran":
                await LogAsync(cn, ct, injected, 107, 5007, "5.160.1.10", "IR", "Access", "/api/qbank/package/12/questions");
                break;
            case "emb-north-korea":
                await LogAsync(cn, ct, injected, 108, 5008, "175.45.176.3", "KP", "Access", "/api/qbank/package/10/questions");
                break;
            case "blk-combo":
                await LogAsync(cn, ct, injected, 109, 5009, "45.66.77.88", "IR", "Access", "/api/qbank/package/11/questions");
                break;

            // ---- authorization gap ----
            case "gap-denied":
                await AuthGapAsync(cn, ct, injected, tag, 4, "Denied", 40, "/qbank/package/40/q/" + Random.Shared.Next(100, 999), new[] { 61000 });
                break;
            case "gap-missing":
                await AuthGapAsync(cn, ct, injected, tag, 8, "Missing", 30, "/qbank/package/30/q/" + Random.Shared.Next(100, 999), new[] { 48000 });
                break;
            case "gap-export":
                await AuthGapAsync(cn, ct, injected, tag, 4, "Denied", 40, "/qbank/package/40/export.pdf", new[] { 2_400_000 });
                break;
            case "gap-replay":
                await AuthGapAsync(cn, ct, injected, tag, 8, "Denied", 30, "/qbank/package/30/q/" + Random.Shared.Next(100, 999), new[] { 52000, 51000, 53500 });
                break;
            case "gap-stale-cache":
                await AuthGapAsync(cn, ct, injected, tag, 3, "Denied", 10, "/cdn/qbank/package/10/q/" + Random.Shared.Next(100, 999), new[] { 18000 });
                break;
        }

        var created = await _worker.RunOnceAsync(ct, requestId);
        var newCases = await NewCasesAsync(cn, beforeCaseId, ct);
        return new SimulationResult(key, tag, created, injected, newCases);
    }

    // ---------------------------------------------------------------- injectors

    private static async Task<int> PaymentAsync(SqlConnection cn, CancellationToken ct, List<string> injected,
        int userId, decimal amount, string? txnId, short pkg, string? code = null, decimal discount = 0m,
        byte modeId = 1, byte sourceId = 2, bool settle = false)
    {
        var id = await ScalarIntAsync(cn, "SELECT ISNULL(MAX(PaymentId), 0) + 1 FROM dbo.USER_PAYMENTS", ct);
        await Exec(cn, ct,
            @"INSERT dbo.USER_PAYMENTS (PaymentId,UserId,FinalAmount,TransactionAmount,DiscountAmount,TransactionId,PaymentStatus,GatewayId,PaymentModeId,ResponseCode,SourceId,PaymentTypeId,IPAddress,CountryCode,DateCreated,DatePaymentReceived)
              VALUES (@id, @u, @amt, @amt, @disc, @txn, 1, 4, @mode, 1, @src, 1, '203.0.113.200', 'US', SYSUTCDATETIME(), SYSUTCDATETIME())",
            ("@id", id), ("@u", userId), ("@amt", amount), ("@disc", discount), ("@txn", (object?)txnId ?? DBNull.Value),
            ("@mode", modeId), ("@src", sourceId));
        injected.Add($"USER_PAYMENTS #{id}: user {userId}, ${amount}, Paid, gateway 4, mode {modeId}, source {sourceId}, txnId {(txnId is null ? "NULL" : "'" + txnId + "'")}");
        await DetailAsync(cn, ct, injected, id, pkg, code, amount + discount, discount);

        if (settle && txnId is not null)
        {
            // a genuine gateway row so only the discount rule (not payment integrity) fires
            await Exec(cn, ct,
                @"INSERT dbo.BRAINTREE_TRANSACTIONS (TransactionId,PaymentId,TransactionStatus,Amount,IsMismatch,IsVerified,IsFraudDetected,DateCreated)
                  VALUES (@t, @p, 'settled', @amt, 0, 1, 0, SYSUTCDATETIME())", ("@t", txnId), ("@p", id), ("@amt", amount));
        }
        return id;
    }

    private static async Task DetailAsync(SqlConnection cn, CancellationToken ct, List<string> injected,
        int paymentId, short pkg, string? code, decimal baseAmount, decimal discount)
    {
        await Exec(cn, ct,
            @"INSERT dbo.USER_PAYMENT_DETAILS (PaymentId,PackageId,CourseId,DiscountCode,BaseAmount,DiscountAmount,FinalAmount)
              VALUES (@p, @pkg, 1, @code, @base, @disc, @base - @disc)",
            ("@p", paymentId), ("@pkg", pkg), ("@code", (object?)code ?? DBNull.Value), ("@base", baseAmount), ("@disc", discount));
        injected.Add($"USER_PAYMENT_DETAILS: payment {paymentId}, package {pkg}{(code is null ? "" : $", code {code} (-${discount})")}");
    }

    private static async Task RegionAsync(SqlConnection cn, CancellationToken ct, List<string> injected, int userId,
        (string Country, string Ip, int Device, int MinutesAgo)[] hits)
    {
        var sub = await ScalarIntAsync(cn, "SELECT ISNULL(MAX(SubscriptionId), 5900) + 1 FROM dbo.USER_SUBSCRIPTIONS WHERE SubscriptionId >= 5900", ct);
        await Exec(cn, ct,
            @"INSERT dbo.USER_SUBSCRIPTIONS (SubscriptionId,UserId,CourseId,PackageId,StatusId,Active,DateActivated,DateExpiration,DateFirstAccessed)
              VALUES (@sub, @u, 1, 10, 1, 1, DATEADD(DAY,-10,SYSUTCDATETIME()), DATEADD(DAY,+50,SYSUTCDATETIME()), DATEADD(DAY,-9,SYSUTCDATETIME()))",
            ("@sub", sub), ("@u", userId));
        injected.Add($"USER_SUBSCRIPTIONS #{sub}: user {userId}, package 10, active");
        foreach (var h in hits)
        {
            await Exec(cn, ct,
                @"INSERT dbo.USER_LOGS (UserId,SubscriptionId,IPAddress,CountryCode,DeviceTypeId,LogType,Url,DateCreated)
                  VALUES (@u, @sub, @ip, @cc, @dev, 'Access', '/api/qbank/package/10/access', DATEADD(MINUTE, @m, SYSUTCDATETIME()))",
                ("@u", userId), ("@sub", sub), ("@ip", h.Ip), ("@cc", h.Country), ("@dev", h.Device), ("@m", h.MinutesAgo));
            injected.Add($"USER_LOGS: sub {sub} from {h.Country} ({h.Ip}, device {h.Device}) {(h.MinutesAgo == 0 ? "now" : -h.MinutesAgo + " min ago")}");
        }
    }

    private static async Task CodeAsync(SqlConnection cn, CancellationToken ct, List<string> injected,
        string code, int limit, int used, int group, decimal percent)
    {
        var id = await ScalarIntAsync(cn, "SELECT ISNULL(MAX(ReferralId), 0) + 1 FROM dbo.NCLEX_REFERRAL_CODES", ct);
        await Exec(cn, ct,
            @"INSERT dbo.NCLEX_REFERRAL_CODES (ReferralId,ReferralCode,ReferralCodeLimit,ReferralCodeUsed,CodeExpirationDate,DiscountGroupId,DiscountBehaviourTypeId,DiscountPercent)
              VALUES (@id, @code, @lim, @used, DATEADD(DAY,30,SYSUTCDATETIME()), @grp, 1, @pct)",
            ("@id", id), ("@code", code), ("@lim", limit), ("@used", used), ("@grp", group), ("@pct", percent));
        injected.Add($"NCLEX_REFERRAL_CODES '{code}': {percent}% off, used {used}/{limit}, group {group}, Regular (non-stackable)");
    }

    private static async Task UsageAsync(SqlConnection cn, CancellationToken ct, List<string> injected,
        string code, int userId, int? paymentId, int secondsAgo)
    {
        await Exec(cn, ct,
            @"INSERT dbo.REFERRAL_CODE_USAGE (ReferralCode,UserId,PaymentId,DateUsed)
              VALUES (@code, @u, @p, DATEADD(SECOND, @s, SYSUTCDATETIME()))",
            ("@code", code), ("@u", userId), ("@p", (object?)paymentId ?? DBNull.Value), ("@s", secondsAgo));
        injected.Add($"REFERRAL_CODE_USAGE: '{code}' by user {userId}{(paymentId is null ? "" : $" on payment {paymentId}")} {(secondsAgo == 0 ? "now" : -secondsAgo + "s ago")}");
    }

    private static async Task ApplyUrlLogAsync(SqlConnection cn, CancellationToken ct, List<string> injected,
        int userId, int subscriptionId, string code, int pkg)
    {
        var url = $"/api/v2/orders/applydiscountcode?code={code}&packageId={pkg}";
        await LogAsync(cn, ct, injected, userId, subscriptionId, "203.0.113.200", "US", "ApplyDiscountCode", url);
    }

    private static async Task LogAsync(SqlConnection cn, CancellationToken ct, List<string> injected,
        int userId, int subscriptionId, string ip, string country, string logType, string url)
    {
        await Exec(cn, ct,
            @"INSERT dbo.USER_LOGS (UserId,SubscriptionId,IPAddress,CountryCode,DeviceTypeId,LogType,Url,DateCreated)
              VALUES (@u, @sub, @ip, @cc, 1, @lt, @url, SYSUTCDATETIME())",
            ("@u", userId), ("@sub", subscriptionId), ("@ip", ip), ("@cc", country), ("@lt", logType), ("@url", url));
        injected.Add($"USER_LOGS: {logType} by user {userId} (sub {subscriptionId}) from {ip} [{country}] → {url}");
    }

    private static async Task AuthGapAsync(SqlConnection cn, CancellationToken ct, List<string> injected, string tag,
        int accountId, string authResult, int packageId, string resource, int[] bytesPerRequest)
    {
        var correlation = "C-SIM-" + tag;
        var authId = await ScalarIntAsync(cn, "SELECT ISNULL(MAX(AuthId), 0) + 1 FROM dbo.AuthOutcomes", ct);
        await Exec(cn, ct,
            @"INSERT dbo.AuthOutcomes (AuthId, AccountId, Resource, AuthResult, CorrelationId, OccurredUtc)
              VALUES (@id, @acct, @res, @result, @corr, SYSUTCDATETIME())",
            ("@id", authId), ("@acct", accountId), ("@res", resource), ("@result", authResult), ("@corr", correlation));
        injected.Add($"AuthOutcomes #{authId}: account {accountId} → {authResult} for {resource} (correlation {correlation})");

        for (var i = 0; i < bytesPerRequest.Length; i++)
        {
            var accessId = await ScalarIntAsync(cn, "SELECT ISNULL(MAX(AccessId), 0) + 1 FROM dbo.AccessEvents", ct);
            await Exec(cn, ct,
                @"INSERT dbo.AccessEvents (AccessId, AccountId, Resource, PackageId, QuestionId, SessionId, ContentReturnedBytes, IpAddress, DeviceId, CorrelationId, OccurredUtc)
                  VALUES (@id, @acct, @res, @pkg, @q, @sess, @bytes, '198.51.100.200', 'DEV-SIM', @corr, DATEADD(SECOND, @i, SYSUTCDATETIME()))",
                ("@id", accessId), ("@acct", accountId), ("@res", resource), ("@pkg", packageId), ("@q", Random.Shared.Next(100, 999)),
                ("@sess", "SIM-" + correlation), ("@bytes", bytesPerRequest[i]), ("@corr", correlation), ("@i", i));
            injected.Add($"AccessEvents #{accessId}: {bytesPerRequest[i]:N0} bytes returned for {resource} (same correlation)");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<IReadOnlyList<SimulatedCase>> NewCasesAsync(SqlConnection cn, int afterCaseId, CancellationToken ct)
    {
        var list = new List<SimulatedCase>();
        await using var cmd = new SqlCommand(
            "SELECT CaseId, RuleId, Severity, Title FROM dbo.RiskCases WHERE CaseId > @after ORDER BY CaseId", cn);
        cmd.Parameters.AddWithValue("@after", afterCaseId);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            list.Add(new SimulatedCase(rd.GetInt32(0), rd.GetString(1), rd.GetValue(2).ToString()!, rd.GetString(3)));
        return list;
    }

    private static async Task Exec(SqlConnection cn, CancellationToken ct, string sql, params (string, object)[] ps)
    {
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> ScalarIntAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }
}
