namespace QuantBacktest.Core.Errors;

public enum MarketDataErrorKind
{
    MissingApiKey = 0,
    RateLimitExceeded = 1,
    SymbolNotFound = 2,
    NetworkError = 3,
    InvalidResponse = 4,

    /// <summary>The operation needs cached data and there is none for this symbol and timeframe.</summary>
    NotCached = 5,
}

/// <summary>Raised by IMarketDataService. The message is English, safe to show, and never contains the API key.</summary>
public sealed class MarketDataException : Exception
{
    public MarketDataException(MarketDataErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public MarketDataErrorKind Kind { get; }
}

/// <summary>
/// Raised when a user request is invalid (unknown strategy, parameter out of range, not enough bars,
/// invalid custom rule...). The message is English and safe to show to the end user.
/// </summary>
public sealed class BacktestValidationException : Exception
{
    public BacktestValidationException(string message)
        : base(message)
    {
    }
}
