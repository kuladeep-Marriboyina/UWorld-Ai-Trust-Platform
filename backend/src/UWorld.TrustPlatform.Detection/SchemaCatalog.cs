using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

/// <summary>
/// Every table and column an authored rule may use, with type, meaning and allowed values.
/// The rule builder renders its pickers from this, and the AI may only map English onto these fields.
/// </summary>
public static class SchemaCatalogFactory
{
    public static SchemaCatalog Build()
    {
        var targets = new[]
        {
            new SchemaTarget("USERS", "One row per account created (registration identity: email, IP, device).", "DateCreated", "account"),
            new SchemaTarget("API_LOGS", "Every customer API call: register, login, subscribe, apply code.", "DateCreated", "request"),
            new SchemaTarget("USER_PAYMENTS", "One row per order/payment, with gateway and card fingerprint.", "DateCreated", "payment"),
            new SchemaTarget("USER_SUBSCRIPTIONS", "One row per subscription granted.", "DateActivated", "subscription"),
            new SchemaTarget("USER_LOGS", "Content access / login activity with geo.", "DateCreated", "access event"),
            new SchemaTarget("REFERRAL_CODE_USAGE", "One row each time a referral/discount code is redeemed.", "DateUsed", "redemption"),
            new SchemaTarget("NCLEX_REFERRAL_CODES", "One row per discount/referral code (limits and usage counters).", "", "code"),
        };

        return new SchemaCatalog
        {
            Targets = targets.Select(t => t.Table).ToArray(),
            TargetInfo = targets,
            Fields = new[]
            {
                // USERS
                new SchemaField("USERS", "Email", "string", new[]{"email","email address"}),
                new SchemaField("USERS", "EmailDomain", "string", new[]{"domain","email domain","provider"}, "derived: part after @"),
                new SchemaField("USERS", "EmailPattern", "string", new[]{"email pattern","template","similar emails","same format"}, "derived: local part with digits removed (jake.miller07 → jake.miller)"),
                new SchemaField("USERS", "Mailbox", "string", new[]{"same inbox","alias","plus address"}, "derived: real mailbox (strips +tags, gmail dots)"),
                new SchemaField("USERS", "IsDisposableEmail", "bool", new[]{"disposable","temporary email","burner","mailinator"}),
                new SchemaField("USERS", "RegistrationIp", "string", new[]{"ip","signup ip","same ip","ip address","network"}),
                new SchemaField("USERS", "DeviceFingerprint", "string", new[]{"device","same device","fingerprint","browser"}),
                new SchemaField("USERS", "UserAgent", "string", new[]{"user agent","client"}),
                new SchemaField("USERS", "IsAutomationClient", "bool", new[]{"bot","script","automated","automation"}),
                new SchemaField("USERS", "CountryCode", "string", new[]{"country","region"}),
                new SchemaField("USERS", "FirstName", "string", new[]{"first name","name"}),
                new SchemaField("USERS", "LastName", "string", new[]{"last name","surname"}),
                new SchemaField("USERS", "SignupSource", "enum", new[]{"source","channel"}, "Web, Mobile"),
                new SchemaField("USERS", "UserId", "int", new[]{"user","account"}),
                // API_LOGS
                new SchemaField("API_LOGS", "Endpoint", "string", new[]{"endpoint","api","url","login","register","signup"}, "/api/auth/login, /api/auth/register, /api/orders/subscribe, /api/orders/applydiscountcode"),
                new SchemaField("API_LOGS", "Outcome", "enum", new[]{"failed","success","outcome","rejected"}, "Success, Failed, Rejected"),
                new SchemaField("API_LOGS", "StatusCode", "int", new[]{"status","http status","401","error"}),
                new SchemaField("API_LOGS", "IPAddress", "string", new[]{"ip","source","same ip","ip address"}),
                new SchemaField("API_LOGS", "Email", "string", new[]{"email","account attempted","username"}),
                new SchemaField("API_LOGS", "UserId", "int", new[]{"user","account"}),
                new SchemaField("API_LOGS", "DeviceFingerprint", "string", new[]{"device"}),
                new SchemaField("API_LOGS", "UserAgent", "string", new[]{"user agent","client"}),
                new SchemaField("API_LOGS", "IsAutomationClient", "bool", new[]{"bot","script","automated"}),
                new SchemaField("API_LOGS", "CountryCode", "string", new[]{"country","region","geo"}),
                // USER_PAYMENTS
                new SchemaField("USER_PAYMENTS", "PaymentStatus", "enum", new[]{"paid","payment status","status"}, "1=Paid, 2=Unpaid, 3=Partial, 4=Cancelled"),
                new SchemaField("USER_PAYMENTS", "FinalAmount", "money", new[]{"amount","order total","final amount","price","value","paid amount"}),
                new SchemaField("USER_PAYMENTS", "TransactionAmount", "money", new[]{"charged","transaction amount","amount charged"}),
                new SchemaField("USER_PAYMENTS", "DiscountAmount", "money", new[]{"discount","discount amount"}),
                new SchemaField("USER_PAYMENTS", "GatewayId", "enum", new[]{"gateway","braintree","payment gateway"}, "4=Braintree, 2=PayPal"),
                new SchemaField("USER_PAYMENTS", "TransactionId", "string", new[]{"transaction id","gateway id","braintree id"}, "'0' / '0000' / empty = no gateway transaction"),
                new SchemaField("USER_PAYMENTS", "PaymentModeId", "enum", new[]{"payment mode","store credit","check","wire","card"}, "1=Card, 2=Check, 3=Wire/ACH, 6=StoreCredit"),
                new SchemaField("USER_PAYMENTS", "PaymentTypeId", "enum", new[]{"payment type","trial","complimentary","free"}, "1=Standard, 5=StudentOrder, 7=Complimentary/Trial"),
                new SchemaField("USER_PAYMENTS", "CardFingerprint", "string", new[]{"card","same card","credit card","payment method"}),
                new SchemaField("USER_PAYMENTS", "IPAddress", "string", new[]{"ip","checkout ip"}),
                new SchemaField("USER_PAYMENTS", "CountryCode", "string", new[]{"country","region","from country"}),
                new SchemaField("USER_PAYMENTS", "UserId", "int", new[]{"user","account","customer"}),
                // USER_SUBSCRIPTIONS
                new SchemaField("USER_SUBSCRIPTIONS", "Active", "bool", new[]{"active","current"}),
                new SchemaField("USER_SUBSCRIPTIONS", "PackageId", "int", new[]{"package","product","qbank"}, "10 Step 1, 11 Step 2 CK, 12 NCLEX, 20 Bundle, 30 CPA, 40 MCAT"),
                new SchemaField("USER_SUBSCRIPTIONS", "ListPrice", "money", new[]{"list price","value","price"}),
                new SchemaField("USER_SUBSCRIPTIONS", "UserId", "int", new[]{"user","account"}),
                // USER_LOGS
                new SchemaField("USER_LOGS", "CountryCode", "string", new[]{"country","region","geo"}),
                new SchemaField("USER_LOGS", "IPAddress", "string", new[]{"ip","ip address"}),
                new SchemaField("USER_LOGS", "SubscriptionId", "int", new[]{"subscription"}),
                new SchemaField("USER_LOGS", "LogType", "enum", new[]{"log type","access","login","apply code"}, "Access, Login, ApplyDiscountCode"),
                new SchemaField("USER_LOGS", "UserId", "int", new[]{"user","account"}),
                // codes
                new SchemaField("REFERRAL_CODE_USAGE", "ReferralCode", "string", new[]{"code","coupon","promo","referral"}),
                new SchemaField("REFERRAL_CODE_USAGE", "UserId", "int", new[]{"user","account"}),
                new SchemaField("NCLEX_REFERRAL_CODES", "ReferralCode", "string", new[]{"code","coupon"}),
                new SchemaField("NCLEX_REFERRAL_CODES", "ReferralCodeUsed", "int", new[]{"times used","usage count","uses"}),
                new SchemaField("NCLEX_REFERRAL_CODES", "ReferralCodeLimit", "int", new[]{"limit","max uses","usage limit"}),
                new SchemaField("NCLEX_REFERRAL_CODES", "DiscountPercent", "number", new[]{"percent off","discount percent"}),
                new SchemaField("NCLEX_REFERRAL_CODES", "DiscountBehaviourTypeId", "enum", new[]{"stackable","behaviour"}, "1=Regular, 2=Stackable, 3=MultiStackable"),
            }
        };
    }
}

/// <summary>A tunable parameter of a coded rule, rendered as a typed, validated form field in the portal.</summary>
public sealed record RuleParameter(string Key, string Label, string Type, double? Default, double? Min, double? Max, string? Unit, string Help, string[]? DefaultList = null);

public static class RuleParameterCatalog
{
    public static readonly IReadOnlyDictionary<string, RuleParameter[]> ByRule = new Dictionary<string, RuleParameter[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["account-farm"] = new[]
        {
            new RuleParameter("windowMinutes", "Linking window", "int", 60, 5, 1440, "minutes", "Signups from the same IP this close together are linked into one cluster."),
            new RuleParameter("minAccounts", "Minimum accounts", "int", 3, 2, 50, "accounts", "A cluster must contain at least this many accounts to raise a case."),
            new RuleParameter("alertScore", "Alert score", "int", 55, 30, 95, "points", "Corroborated clusters at or above this score raise a case. Lower = earlier alerts, more noise."),
            new RuleParameter("lookbackHours", "Lookback", "int", 168, 1, 720, "hours", "How far back new accounts are considered."),
        },
        ["credential-stuffing"] = new[]
        {
            new RuleParameter("windowMinutes", "Burst window", "int", 15, 1, 240, "minutes", "Failed logins inside this window count as one burst."),
            new RuleParameter("minFailures", "Minimum failures", "int", 8, 3, 500, "attempts", "Failed logins from one source needed to raise a case."),
            new RuleParameter("minDistinctAccounts", "Minimum accounts targeted", "int", 5, 2, 500, "accounts", "Distinct accounts the failures must spread across (separates attacks from forgotten passwords)."),
        },
        ["discount-code-fraud"] = new[]
        {
            new RuleParameter("raceWindowSeconds", "Race window", "int", 60, 1, 3600, "seconds", "Two different users redeeming one code within this gap = counter race."),
            new RuleParameter("multipleUseCodes", "Multi-use codes", "list", null, null, null, null, "Codes that are allowed to be reused by one user (excluded from reuse checks).", new[]{"apcmb"}),
        },
        ["same-subscription-region"] = new[]
        {
            new RuleParameter("windowHours", "Window", "int", 24, 1, 168, "hours", "Countries seen for one subscription within this window are compared."),
            new RuleParameter("minCountries", "Minimum countries", "int", 2, 2, 10, "countries", "Distinct countries needed to raise a case."),
        },
        ["paid-without-braintree"] = new[]
        {
            new RuleParameter("braintreeGatewayId", "Braintree gateway id", "int", 4, 1, 20, null, "GatewayId that represents Braintree card capture."),
            new RuleParameter("minAmount", "Minimum amount", "number", 0.01, 0, 10000, "USD", "Orders below this are ignored ($0 trials and store credit)."),
            new RuleParameter("nullTxnIds", "Placeholder transaction ids", "list", null, null, null, null, "Values that mean 'no gateway transaction'.", new[]{"0","0000",""}),
        },
    };
}
