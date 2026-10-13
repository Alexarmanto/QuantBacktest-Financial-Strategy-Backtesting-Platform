namespace QuantBacktest.Core.Models;

/// <summary>
/// How orders are filled. All rates are fractions (0.001 = 0.1 %). A null stop or target is disabled.
/// Exact rules: docs/CONTRACT.md section 4.
/// </summary>
public sealed record ExecutionSettings(
    decimal InitialCapital = Conventions.DefaultInitialCapital,
    decimal FeeRate = Conventions.DefaultFeeRate,
    decimal SlippageRate = Conventions.DefaultSlippageRate,
    decimal? StopLossFraction = null,
    decimal? TakeProfitFraction = null,
    decimal? TrailingStopFraction = null,
    double RiskFreeRate = Conventions.DefaultRiskFreeRate)
{
    public static ExecutionSettings Default { get; } = new();
}

/// <summary>Everything needed to run one backtest. From and To are inclusive; null means series bounds.</summary>
public sealed record BacktestRequest(
    string Symbol,
    Timeframe Timeframe,
    string StrategyId,
    IReadOnlyDictionary<string, double> Parameters,
    DateOnly? From,
    DateOnly? To,
    ExecutionSettings Execution);

public enum ExitReason
{
    Signal = 0,
    StopLoss = 1,
    TakeProfit = 2,
    TrailingStop = 3,
    EndOfData = 4,
}

/// <summary>
/// One completed round trip (long only). Prices are fill prices (slippage included). NetProfit is after fees.
/// ReturnFraction = NetProfit / (Quantity * EntryPrice + entry fees). HoldingBars: docs/CONTRACT.md rule X12.
/// </summary>
public sealed record Trade(
    DateOnly EntryDate,
    decimal EntryPrice,
    DateOnly ExitDate,
    decimal ExitPrice,
    decimal Quantity,
    decimal Fees,
    decimal NetProfit,
    double ReturnFraction,
    ExitReason ExitReason,
    int HoldingBars);

/// <summary>
/// Portfolio values at the close of one bar. Drawdowns are non-negative fractions (0.23 = 23 % below peak).
/// InPosition: a position was held at this close (an end-of-data liquidation at this close counts as held).
/// </summary>
public sealed record EquityPoint(
    DateOnly Date,
    decimal StrategyEquity,
    decimal BenchmarkEquity,
    double StrategyDrawdown,
    double BenchmarkDrawdown,
    bool InPosition);

/// <summary>
/// All ratios are fractions, not percentages (0.12 = 12 %). MaxDrawdown and Exposure are in [0, 1].
/// Nullable ratios are null when mathematically undefined (shown as "n/a"). No field is ever NaN or infinite.
/// Formulas: docs/CONTRACT.md section 5.
/// </summary>
public sealed record PerformanceMetrics(
    decimal FinalEquity,
    double CumulativeReturn,
    double AnnualizedReturn,
    double AverageReturnPerPeriod,
    double AnnualizedVolatility,
    double? SharpeRatio,
    double? SortinoRatio,
    double? CalmarRatio,
    double MaxDrawdown,
    int TradeCount,
    double WinRate,
    double? ProfitFactor,
    double Exposure);

/// <summary>
/// Complete output of one backtest. Bars are the bars of the tested period only (not the warm-up bars).
/// Equity has exactly one point per bar of Bars; each IndicatorSeries has exactly one value per bar of Bars.
/// </summary>
public sealed record BacktestResult(
    BacktestRequest Request,
    string StrategyName,
    bool PricesAdjusted,
    IReadOnlyList<PriceBar> Bars,
    IReadOnlyList<EquityPoint> Equity,
    IReadOnlyList<Trade> Trades,
    PerformanceMetrics Metrics,
    PerformanceMetrics BenchmarkMetrics,
    IReadOnlyList<IndicatorSeries> Indicators);
