using System.Text.Json;
using QuantBacktest.Core.Errors;

namespace QuantBacktest.Data.AlphaVantage;

/// <summary>
/// Maps the JSON errors that Alpha Vantage sends with HTTP 200 to a <see cref="MarketDataException"/> (D4, P1-D4).
/// The Alpha Vantage text is never copied into the message: the rate limit notice repeats the API key.
/// </summary>
internal static class AlphaVantageErrorClassifier
{
    // Checked before "premium": the rate limit notice also invites to "subscribe to any of the premium plans".
    private static readonly string[] RateLimitMarkers = ["rate limit", "call frequency", "requests per", "calls per"];

    public static MarketDataException Classify(string json, string symbol)
    {
        var (key, text) = ReadFirstKnownField(json);
        return key switch
        {
            "Error Message" => SymbolNotFound(symbol),
            "Note" => RateLimitExceeded(),
            "Information" when ContainsAny(text, RateLimitMarkers) => RateLimitExceeded(),
            "Information" when text.Contains("premium", StringComparison.OrdinalIgnoreCase) => new MarketDataException(
                MarketDataErrorKind.InvalidResponse,
                "This Alpha Vantage feature requires a premium plan."),
            _ => new MarketDataException(
                MarketDataErrorKind.InvalidResponse,
                "Alpha Vantage returned an unexpected response."),
        };
    }

    private static (string? Key, string Text) ReadFirstKnownField(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "Error Message", "Note", "Information" })
                {
                    if (document.RootElement.TryGetProperty(key, out var value))
                    {
                        return (key, value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON is an unexpected response, reported by the caller as InvalidResponse.
        }

        return (null, string.Empty);
    }

    private static bool ContainsAny(string text, string[] markers)
        => markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static MarketDataException SymbolNotFound(string symbol) => new(
        MarketDataErrorKind.SymbolNotFound,
        $"Alpha Vantage has no data for the symbol '{symbol}'.");

    private static MarketDataException RateLimitExceeded() => new(
        MarketDataErrorKind.RateLimitExceeded,
        "The Alpha Vantage request limit has been reached. Try again later or use cached data.");
}
