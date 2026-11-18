using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Credential stuffing over API_LOGS: one source fails logins against many different accounts in a short burst
/// (a leaked password list being replayed). Ordinary users mistype their own password — they don't fail across
/// five strangers' accounts in minutes. A successful login inside the burst is a probable account takeover.
/// </summary>
public sealed class CredentialStuffingRule : IDetectionRule
{
    public string RuleId => "credential-stuffing";
    public string Version => "v1";
    public string Category => "Account Takeover";

    private readonly RuleConfigStore _config;
    public CredentialStuffingRule(RuleConfigStore config) => _config = config;

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var window = TimeSpan.FromMinutes(_config.Num(RuleId, "windowMinutes", 15));
        var minFailures = _config.Int(RuleId, "minFailures", 8);
        var minDistinct = _config.Int(RuleId, "minDistinctAccounts", 5);
        var since = s.EvaluatedUtc.AddHours(-24);

        var logins = s.ApiLogs.Where(l => l.DateCreated >= since && l.Endpoint.Contains("/auth/login", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var byIp in logins.GroupBy(l => l.IPAddress))
        {
            var ordered = byIp.OrderBy(l => l.DateCreated).ToList();
            var failures = ordered.Where(l => l.Outcome == "Failed").ToList();
            if (failures.Count < minFailures) continue;

            // first window that qualifies, then greedily extend the burst while failures keep arriving within the window
            for (var i = 0; i < failures.Count; i++)
            {
                var inWin = failures.Where(f => f.DateCreated >= failures[i].DateCreated && f.DateCreated <= failures[i].DateCreated + window).ToList();
                if (inWin.Count < minFailures || inWin.Select(f => f.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count() < minDistinct) continue;

                var burst = new List<ApiLogRecord>(inWin);
                var end = inWin[^1].DateCreated;
                foreach (var f in failures.Where(f => f.DateCreated > end))
                {
                    if (f.DateCreated - end > window) break;
                    burst.Add(f); end = f.DateCreated;
                }
                var start = burst[0].DateCreated;
                var targeted = burst.Select(f => f.Email ?? "?").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var successes = ordered.Where(l => l.Outcome == "Success" && l.DateCreated >= start && l.DateCreated <= end + window).ToList();
                var takenOver = successes.Where(x => x.UserId != null).Select(x => x.UserId!.Value).Distinct().ToList();
                var uas = burst.Select(f => f.UserAgent).Where(u => u != null).Distinct().ToList();
                var bot = uas.FirstOrDefault(IdentityAnalysis.IsAutomationClient);

                var factors = new List<ScoreFactor>
                {
                    new("FailedLoginBurst", Math.Min(40m, 20m + burst.Count), $"{burst.Count} failed logins from {byIp.Key} in {AccountFarmRule.Human(end - start)}."),
                    new("AccountSpread", Math.Min(30m, 10m + 2m * targeted.Count), $"Failures spread across {targeted.Count} different accounts — a list is being replayed, not a forgotten password."),
                };
                if (takenOver.Count > 0)
                    factors.Add(new("SuccessfulTakeover", 25m, $"{takenOver.Count} login(s) succeeded from the same source during the burst — account(s) {string.Join(", ", takenOver)} likely compromised."));
                if (bot != null) factors.Add(new("AutomationClient", 10m, $"Scripted client: {bot}."));
                var score = Math.Min(99m, factors.Sum(f => f.Weight));

                var activeSubs = s.Subscriptions.Where(x => takenOver.Contains(x.UserId) && x.Active && x.DateExpiration > s.EvaluatedUtc).ToList();
                var impact = activeSubs.Sum(x => s.PriceOf(x.PackageId));

                var rc = new RiskCase
                {
                    CaseKey = $"{RuleId}:{byIp.Key}:{burst[0].Id}",
                    RuleId = RuleId, RuleVersion = Version, Category = Category,
                    Title = takenOver.Count > 0
                        ? $"Account takeover: {takenOver.Count} account(s) breached after {burst.Count} failed logins from {byIp.Key}"
                        : $"Credential stuffing: {burst.Count} failed logins across {targeted.Count} accounts from {byIp.Key}",
                    Severity = takenOver.Count > 0 ? RiskSeverity.Critical : RiskSeverity.High,
                    Confidence = takenOver.Count > 0 ? 0.93m : 0.86m,
                    Score = score, ScoreBreakdown = factors,
                    BusinessImpactUsd = impact,
                    ImpactFormula = takenOver.Count == 0
                        ? "No login succeeded — attack contained before any account was taken over ($0 lost)."
                        : activeSubs.Count == 0
                            ? $"{takenOver.Count} account(s) breached but none hold an active paid subscription ($0 direct revenue; customer-trust impact)."
                            : $"{activeSubs.Count} active subscription(s) on breached accounts at list price = ${impact:N2} of paid access now in an attacker's hands.",
                    AccountId = takenOver.Count > 0 ? takenOver[0] : null,
                    EntityRef = $"ip:{byIp.Key}",
                    Expected = "Failed logins come from account owners mistyping their own password — a few attempts against one account.",
                    Actual = $"{byIp.Key} attempted {targeted.Count} different accounts with {burst.Count} failures between {start:HH:mm:ss} and {end:HH:mm:ss} UTC" +
                             (takenOver.Count > 0 ? $", then logged in successfully as account(s) {string.Join(", ", takenOver)}." : "; no login succeeded."),
                    FirstSignalUtc = start,
                    TriggerSignalUtc = successes.Count > 0 ? successes.Max(x => x.DateCreated) > end ? successes.Max(x => x.DateCreated) : end : end,
                };
                foreach (var f in burst.Take(40))
                    rc.Evidence.Add(Ev("ApiLog", f.Id.ToString(), $"401 login as {f.Email}", $"ip={f.IPAddress} · ua={f.UserAgent} · device={f.DeviceFingerprint}", f.DateCreated));
                foreach (var x in successes)
                    rc.Evidence.Add(Ev("ApiLog", x.Id.ToString(), $"200 login as {x.Email} (account {x.UserId})", $"ip={x.IPAddress} · ua={x.UserAgent}", x.DateCreated));

                rc.Entities.Add(new CaseEntity("ip", byIp.Key));
                foreach (var d in burst.Select(f => f.DeviceFingerprint).Where(d => d != null).Distinct().Take(3)) rc.Entities.Add(new CaseEntity("device", d!));
                foreach (var e in targeted.Take(30)) rc.Entities.Add(new CaseEntity("email", e));
                foreach (var u in takenOver) rc.Entities.Add(new CaseEntity("user", u.ToString()));

                if (takenOver.Count > 0)
                    rc.Playbook.Add($"Force a password reset and revoke all sessions for account(s) {string.Join(", ", takenOver)} now.");
                rc.Playbook.Add($"Challenge (CAPTCHA) or rate-limit logins from {byIp.Key}; block it if attempts continue.");
                rc.Playbook.Add($"Notify the {targeted.Count} targeted account holders and recommend unique passwords.");
                rc.Playbook.Add("Check the targeted emails against known breach corpora to size the exposure.");

                yield return rc;
                break;   // one case per source per evaluation; the burst extends as new failures arrive
            }
        }
    }
}
