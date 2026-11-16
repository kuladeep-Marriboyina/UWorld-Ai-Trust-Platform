using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

/// <summary>
/// The field catalog the AI uses to map plain-English rules onto real schema fields.
/// Each field lists its table, type, and English synonyms (what an analyst might say).
/// </summary>
public static class SchemaCatalogFactory
{
    public static SchemaCatalog Build() => new()
    {
        Targets = new[] { "USER_PAYMENTS", "USER_LOGS", "NCLEX_REFERRAL_CODES", "USER_SUBSCRIPTIONS", "REFERRAL_CODE_USAGE" },
        Fields = new[]
        {
            new SchemaField("USER_PAYMENTS", "PaymentStatus", "enum", new[]{"paid","payment status","status"}, "1=Paid,2=Unpaid,3=Partial,4=Cancelled"),
            new SchemaField("USER_PAYMENTS", "FinalAmount", "money", new[]{"amount","order total","final amount","price","value"}),
            new SchemaField("USER_PAYMENTS", "TransactionAmount", "money", new[]{"charged","transaction amount","amount charged"}),
            new SchemaField("USER_PAYMENTS", "GatewayId", "enum", new[]{"gateway","braintree","payment gateway"}, "4=Braintree,2=PayPal"),
            new SchemaField("USER_PAYMENTS", "TransactionId", "string", new[]{"transaction id","gateway id","braintree id"}, "'0'/'0000' = none"),
            new SchemaField("USER_PAYMENTS", "PaymentModeId", "enum", new[]{"payment mode","store credit","check","wire"}, "1=Card,6=StoreCredit"),
            new SchemaField("USER_PAYMENTS", "CountryCode", "string", new[]{"country","region","from country"}),
            new SchemaField("USER_PAYMENTS", "UserId", "int", new[]{"user","account","customer"}),
            new SchemaField("USER_LOGS", "CountryCode", "string", new[]{"country","region","geo"}),
            new SchemaField("USER_LOGS", "IPAddress", "string", new[]{"ip","ip address"}),
            new SchemaField("USER_LOGS", "SubscriptionId", "int", new[]{"subscription"}),
            new SchemaField("USER_LOGS", "DeviceTypeId", "enum", new[]{"device"}),
            new SchemaField("NCLEX_REFERRAL_CODES", "ReferralCodeUsed", "int", new[]{"times used","usage count","uses"}),
            new SchemaField("NCLEX_REFERRAL_CODES", "ReferralCodeLimit", "int", new[]{"limit","max uses","usage limit"}),
            new SchemaField("NCLEX_REFERRAL_CODES", "DiscountPercent", "number", new[]{"percent off","discount percent"}),
            new SchemaField("NCLEX_REFERRAL_CODES", "DiscountBehaviourTypeId", "enum", new[]{"stackable","behaviour"}, "1=Regular,2=Stackable,3=MultiStackable"),
            new SchemaField("USER_SUBSCRIPTIONS", "Active", "bool", new[]{"active","current"}),
            new SchemaField("USER_SUBSCRIPTIONS", "PackageId", "int", new[]{"package"}),
            new SchemaField("USER_SUBSCRIPTIONS", "CourseId", "int", new[]{"course"}),
        }
    };
}
