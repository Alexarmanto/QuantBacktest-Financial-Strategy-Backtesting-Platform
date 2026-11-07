using QuantBacktest.Core.Errors;

namespace QuantBacktest.Web.Models;

/// <summary>
/// General message at the top of the form (P4-U12) for a request refused by a service. The message comes from the
/// exception (English and safe to show, docs/CONTRACT.md section 12); some kinds get a hint and a command.
/// </summary>
internal sealed record FormAlert(string Message, string? Hint = null, string? Command = null)
{
    public const string RateLimitHint = "Try a symbol that is already cached, or retry tomorrow.";
    public const string MissingApiKeyHint = "Set your Alpha Vantage API key, then run the backtest again:";
    public const string MissingApiKeyCommand =
        "dotnet user-secrets set \"AlphaVantage:ApiKey\" \"<your-key>\" --project src/QuantBacktest.Web";

    /// <summary>MissingApiKey hint of the data pages (P4-U10): nothing is "run" there, the refresh is tried again.</summary>
    public const string MissingApiKeyRetryHint = "Set your Alpha Vantage API key, then try again:";

    public static FormAlert From(BacktestValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new FormAlert(exception.Message);
    }

    public static FormAlert From(MarketDataException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Kind switch
        {
            MarketDataErrorKind.RateLimitExceeded => new FormAlert(exception.Message, RateLimitHint),
            MarketDataErrorKind.MissingApiKey => new FormAlert(exception.Message, MissingApiKeyHint, MissingApiKeyCommand),
            _ => new FormAlert(exception.Message),
        };
    }

    /// <summary>
    /// The alert of <see cref="From(MarketDataException)"/> for the data pages only: same hints, except that the
    /// MissingApiKey hint says "try again" (the backtest forms keep their own wording, unchanged).
    /// </summary>
    public static FormAlert ForRefresh(MarketDataException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Kind == MarketDataErrorKind.MissingApiKey
            ? new FormAlert(exception.Message, MissingApiKeyRetryHint, MissingApiKeyCommand)
            : From(exception);
    }
}
