namespace QuantBacktest.Core.Models;

/// <summary>Sampling frequency of a price series and of a backtest.</summary>
public enum Timeframe
{
    Daily = 0,
    Weekly = 1,
}

/// <summary>
/// One OHLCV bar. When the owning series says <c>IsAdjusted = true</c>, all four prices are already
/// adjusted for splits and dividends. Adjustment is the data layer's job, never the engine's.
/// </summary>
public sealed record PriceBar(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

/// <summary>
/// A price history for one symbol and one timeframe.
/// Invariants guaranteed by every IMarketDataService implementation: bars sorted by date ascending,
/// unique dates, strictly positive prices, High at least max(Open, Close), Low at most min(Open, Close).
/// </summary>
public sealed record PriceSeries(
    string Symbol,
    Timeframe Timeframe,
    IReadOnlyList<PriceBar> Bars,
    bool IsAdjusted);
