namespace UWorld.TrustPlatform.Ai;

/// <summary>Per-token pricing for the approved enterprise LLM. Config today, real rates later.</summary>
public sealed record AiPricing(string Model, decimal InputPerMillionUsd, decimal OutputPerMillionUsd)
{
    public static readonly AiPricing Default = new("enterprise-llm-mock", 3.00m, 15.00m);
}

public sealed record AiCost(string Model, int InputTokens, int OutputTokens, decimal CostUsd);

/// <summary>
/// Estimates token usage and dollar cost for an AI investigation. A crude chars/4 estimate is
/// fine for the PoC; a real client reports exact usage from the model response.
/// </summary>
public sealed class CostCalculator
{
    private readonly AiPricing _pricing;

    public CostCalculator(AiPricing? pricing = null) => _pricing = pricing ?? AiPricing.Default;

    public static int EstimateTokens(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    public AiCost Price(string inputText, string outputText)
    {
        var inTok = EstimateTokens(inputText);
        var outTok = EstimateTokens(outputText);
        var cost = inTok / 1_000_000m * _pricing.InputPerMillionUsd
                 + outTok / 1_000_000m * _pricing.OutputPerMillionUsd;
        return new AiCost(_pricing.Model, inTok, outTok, Math.Round(cost, 6));
    }
}
