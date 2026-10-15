namespace QuantBacktest.Data.Options;

/// <summary>
/// Configuration section "AlphaVantage" (docs/CONTRACT.md section 13, P1-D1).
/// ApiKey only ever comes from user-secrets or the AlphaVantage__ApiKey environment variable.
/// </summary>
internal sealed class AlphaVantageOptions
{
    public const string SectionName = "AlphaVantage";

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://www.alphavantage.co/query";

    public int DailyRequestLimit { get; set; } = 25;
}
