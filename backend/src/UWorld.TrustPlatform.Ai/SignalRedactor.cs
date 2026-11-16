using System.Text.RegularExpressions;
using UWorld.TrustPlatform.Domain;

namespace UWorld.TrustPlatform.Ai;

/// <summary>
/// Strips sensitive values from evidence before anything reaches a model, so the governance
/// promise - "no sensitive data is sent to the model" - is enforced in code, not just policy.
/// </summary>
public interface ISignalRedactor
{
    string Redact(string text);
    CaseEvidence Redact(CaseEvidence evidence);
}

public sealed partial class SignalRedactor : ISignalRedactor
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b")]
    private static partial Regex IpRegex();

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = EmailRegex().Replace(text, "[redacted-email]");
        text = IpRegex().Replace(text, "[redacted-ip]");
        return text;
    }

    public CaseEvidence Redact(CaseEvidence e) => new()
    {
        EvidenceId = e.EvidenceId,
        CaseId = e.CaseId,
        SourceType = e.SourceType,
        SourceId = e.SourceId,
        Label = Redact(e.Label),
        Detail = e.Detail is null ? null : Redact(e.Detail),
        OccurredUtc = e.OccurredUtc
    };
}
