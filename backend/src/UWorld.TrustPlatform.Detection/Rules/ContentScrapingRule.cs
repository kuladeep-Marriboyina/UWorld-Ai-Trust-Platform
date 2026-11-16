using UWorld.TrustPlatform.Domain;
using static UWorld.TrustPlatform.Detection.RuleUtils;

namespace UWorld.TrustPlatform.Detection.Rules;

/// <summary>
/// Separates automated harvesting of the question bank from genuine study by scoring three
/// signals together: raw volume, access rate, and how sequential the question ids are.
/// A human studying jumps around a handful of questions; a scraper walks ids in order, fast.
/// </summary>
public sealed class ContentScrapingRule : IDetectionRule
{
    public string RuleId => "content-scraping";
    public string Version => "v1";
    public string Category => "IP";

    private const int MinQuestions = 50;
    private static readonly TimeSpan MaxWindow = TimeSpan.FromMinutes(15);
    private const double MinSequentialRatio = 0.8;
    private const decimal IpValuePerQuestion = 5m;   // config: finance's valuation of one harvested question

    public IEnumerable<RiskCase> Evaluate(SignalSet s)
    {
        var sessions = s.AccessEvents
            .Where(a => a.QuestionId is not null)
            .GroupBy(a => a.SessionId);

        foreach (var session in sessions)
        {
            var events = session.OrderBy(a => a.OccurredUtc).ToList();
            if (events.Count < MinQuestions) continue;

            var span = events[^1].OccurredUtc - events[0].OccurredUtc;
            if (span > MaxWindow) continue;

            var ids = events.Select(a => a.QuestionId!.Value).OrderBy(x => x).ToList();
            var consecutive = 0;
            for (var i = 1; i < ids.Count; i++)
                if (ids[i] - ids[i - 1] == 1) consecutive++;
            var sequentialRatio = (double)consecutive / (ids.Count - 1);
            if (sequentialRatio < MinSequentialRatio) continue;

            var ratePerMin = events.Count / Math.Max(span.TotalMinutes, 0.1);
            var account = events[0].AccountId;

            yield return new RiskCase
            {
                CaseKey = $"{RuleId}:session:{session.Key}",
                RuleId = RuleId, RuleVersion = Version,
                Title = $"Sequential harvesting of {events.Count} questions in session {session.Key}",
                Severity = RiskSeverity.Critical, Confidence = 0.93m, Score = 90m,
                BusinessImpactUsd = events.Count * IpValuePerQuestion,
                ScoreBreakdown =
                {
                    new ScoreFactor("Volume", 35m, $"{events.Count} questions accessed in one session."),
                    new ScoreFactor("Rate", 25m, $"{ratePerMin:N0} questions/min over {span.TotalMinutes:N1} min."),
                    new ScoreFactor("Sequentiality", 30m, $"{sequentialRatio:P0} of ids were consecutive - machine-like, not study-like."),
                },
                AccountId = account, EntityRef = $"session:{session.Key}",
                Expected = "Genuine study accesses a modest number of non-sequential questions over time.",
                Actual = $"{events.Count} sequential questions ({ids[0]}..{ids[^1]}) in {span.TotalMinutes:N1} min at {ratePerMin:N0}/min.",
                Evidence =
                {
                    Ev("AccessEvent", events[0].AccessId.ToString(), $"First access q{ids[0]}", $"ip={events[0].IpAddress}; device={events[0].DeviceId}", events[0].OccurredUtc),
                    Ev("AccessEvent", events[^1].AccessId.ToString(), $"Last access q{ids[^1]}", $"{events.Count} total in session", events[^1].OccurredUtc),
                }
            };
        }
    }
}
