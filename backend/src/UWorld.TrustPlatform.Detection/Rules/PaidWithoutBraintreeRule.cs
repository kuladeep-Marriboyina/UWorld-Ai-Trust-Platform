using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Payment-integrity gap: an order shows as Paid with a real amount on the Braintree gateway, but
/// there is no matching Braintree transaction (TransactionId is a placeholder, the gateway row is
/// missing, or it's flagged as a mismatch). Legit $0 / trial / store-credit orders are excluded via
/// the TransactionAmount>minAmount gate.
/// </summary>
public sealed class PaidWithoutBraintreeRule : IDetectionRule
{
    public string RuleId => "paid-without-braintree";
    public string Version => "v1";
    public string Category => "Payment";

    private const byte Paid = 1;
    private readonly RuleConfigStore _config;
    public PaidWithoutBraintreeRule(RuleConfigStore config) => _config = config;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var gateway = _config.Int(RuleId, "braintreeGatewayId", 4);
        var minAmount = (decimal)_config.Num(RuleId, "minAmount", 0.01);
        var nullIds = _config.Strings(RuleId, "nullTxnIds");
        if (nullIds.Length == 0) nullIds = new[] { "0", "0000", "" };

        var bt = s.BraintreeTxns.ToDictionary(b => b.TransactionId, b => b, StringComparer.OrdinalIgnoreCase);

        foreach (var p in s.Payments2)
        {
            if (p.PaymentStatus != Paid) continue;
            if (p.TransactionAmount < minAmount) continue;         // real money should have moved
            if (p.GatewayId != gateway) continue;                  // only the card gateway

            var placeholder = string.IsNullOrWhiteSpace(p.TransactionId) || nullIds.Contains(p.TransactionId, StringComparer.OrdinalIgnoreCase);
            var found = !placeholder && bt.TryGetValue(p.TransactionId!, out var txn);
            var mismatch = found && bt[p.TransactionId!].IsMismatch;

            if (!placeholder && found && !mismatch) continue;      // legit: real gateway txn present

            var reason = placeholder ? "no gateway transaction id (placeholder)"
                       : !found ? "no matching Braintree transaction row"
                       : "Braintree transaction flagged IsMismatch";

            var pkg = s.PaymentDetails.Where(x => x.PaymentId == p.PaymentId).Select(x => (short?)x.PackageId).FirstOrDefault();
            var subCtx = SubscriptionContext(s, p.UserId, pkg);
            var sub = ResolveSubscription(s, p.UserId, pkg);

            yield return new RiskCase
            {
                CaseKey = $"{RuleId}:payment:{p.PaymentId}",
                RuleId = RuleId, RuleVersion = Version,
                Title = $"Order {p.PaymentId} marked Paid (${p.TransactionAmount}) but {reason}",
                Severity = RiskSeverity.Critical, Confidence = 0.95m, Score = 94m,
                BusinessImpactUsd = p.FinalAmount,
                ScoreBreakdown =
                {
                    new ScoreFactor("PaymentStatus", 50m, $"PaymentStatus=Paid on gateway {gateway} (Braintree)."),
                    new ScoreFactor("GatewayRecord", 44m, $"Expected a settled Braintree transaction — {reason}."),
                },
                AccountId = p.UserId, EntityRef = $"payment:{p.PaymentId}",
                Expected = "A Paid card order must have a matching, non-mismatched Braintree transaction.",
                Actual = $"Payment {p.PaymentId} · user {p.UserId} · ${p.TransactionAmount} · txnId '{p.TransactionId}' · gateway {gateway} · {p.CountryCode} · {p.DateCreated:u} — {reason}.  · {subCtx}",
                Evidence =
                {
                    Ev("UserPayment", p.PaymentId.ToString(), $"Paid ${p.TransactionAmount} via gateway {gateway}", $"txnId={p.TransactionId}; status=Paid; user={p.UserId}", p.DateCreated),
                    found ? Ev("BraintreeTxn", p.TransactionId!, $"Braintree {bt[p.TransactionId!].TransactionStatus} (IsMismatch={bt[p.TransactionId!].IsMismatch})", null, bt[p.TransactionId!].DateCreated)
                          : Ev("BraintreeTxn", "(none)", "No Braintree transaction found for this payment", null, null),
                    sub != null ? Ev("Subscription", sub.SubscriptionId.ToString(), subCtx, null, LastAccessed(s, sub.SubscriptionId))
                                : Ev("Subscription", "-", subCtx, null, null),
                }
            };
        }
    }
}
