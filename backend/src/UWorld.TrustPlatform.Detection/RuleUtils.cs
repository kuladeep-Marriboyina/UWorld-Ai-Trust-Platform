using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Detection;

internal static class RuleUtils
{
    public static CaseEvidence Ev(string sourceType, string sourceId, string label, string? detail = null, DateTime? occurred = null) =>
        new() { SourceType = sourceType, SourceId = sourceId, Label = label, Detail = detail, OccurredUtc = occurred };

    /// <summary>Resolve the subscription a user/package payment is associated with, with its status + last-accessed time.</summary>
    public static SubscriptionRecord? ResolveSubscription(SignalSet s, int? userId, int? packageId)
    {
        if (userId is null) return null;
        var subs = s.Subscriptions.Where(x => x.UserId == userId).ToList();
        if (packageId is int pk && subs.Any(x => x.PackageId == pk)) subs = subs.Where(x => x.PackageId == pk).ToList();
        return subs.OrderByDescending(x => x.DateExpiration).FirstOrDefault();
    }

    public static DateTime? LastAccessed(SignalSet s, int subscriptionId)
    {
        var logs = s.UserLogs.Where(l => l.SubscriptionId == subscriptionId).Select(l => l.DateCreated).ToList();
        return logs.Count > 0 ? logs.Max() : (DateTime?)null;
    }

    public static string SubscriptionContext(SignalSet s, int? userId, int? packageId)
    {
        var sub = ResolveSubscription(s, userId, packageId);
        if (sub is null) return userId is null ? "no subscription link" : $"user {userId} has no subscription on record";
        var expired = !sub.Active || sub.DateExpiration < s.EvaluatedUtc;
        var last = LastAccessed(s, sub.SubscriptionId) ?? sub.DateFirstAccessed;
        return $"subscription {sub.SubscriptionId} ({(expired ? "EXPIRED" : "Active")}, expires {sub.DateExpiration:u}, last accessed {(last is null ? "never" : last.Value.ToString("u"))})";
    }

    /// <summary>The applydiscountcode URL a user hit nearest the given time, from USER_LOGS.</summary>
    public static string? ApplyDiscountUrl(SignalSet s, int? userId, DateTime near)
    {
        if (userId is null) return null;
        return s.UserLogs
            .Where(l => l.UserId == userId && l.Url != null && l.LogType != null
                        && l.LogType.StartsWith("ApplyDiscount", StringComparison.OrdinalIgnoreCase))
            .OrderBy(l => Math.Abs((l.DateCreated - near).TotalSeconds))
            .Select(l => l.Url)
            .FirstOrDefault();
    }
}
