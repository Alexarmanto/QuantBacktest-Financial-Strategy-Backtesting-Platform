using QuantBacktest.Core.Models;

namespace QuantBacktest.Reports.Tests;

/// <summary>Hand-made backtest results: the report must print them as they are.</summary>
internal static class TestResults
{
    public static readonly PerformanceMetrics StrategyMetrics = new(
        FinalEquity: 12_345.67m,
        CumulativeReturn: 0.2345667,
        AnnualizedReturn: 0.1234,
        AverageReturnPerPeriod: 0.00042,
        AnnualizedVolatility: 0.1875,
        SharpeRatio: 1.2345,
        SortinoRatio: null,
        CalmarRatio: 0.987,
        MaxDrawdown: 0.1234,
        TradeCount: 3,
        WinRate: 2.0 / 3.0,
        ProfitFactor: null,
        Exposure: 0.55);

    public static readonly PerformanceMetrics BenchmarkMetrics = new(
        FinalEquity: 9_500m,
        CumulativeReturn: -0.05,
        AnnualizedReturn: -0.02,
        AverageReturnPerPeriod: -0.0001,
        AnnualizedVolatility: 0.21,
        SharpeRatio: -0.1,
        SortinoRatio: -0.15,
        CalmarRatio: null,
        MaxDrawdown: 0.3,
        TradeCount: 1,
        WinRate: 0,
        ProfitFactor: null,
        Exposure: 1);

    public static BacktestResult Create(
        int barCount = 60,
        int tradeCount = 3,
        Timeframe timeframe = Timeframe.Daily,
        bool pricesAdjusted = false,
        IReadOnlyDictionary<string, double>? parameters = null,
        ExecutionSettings? execution = null,
        IReadOnlyList<IndicatorSeries>? indicators = null)
    {
        var start = new DateOnly(2026, 1, 5);
        var step = timeframe == Timeframe.Weekly ? 7 : 1;
        var bars = Enumerable.Range(0, barCount).Select(i =>
        {
            var close = 100m + (i % 10) - (i / 10);
            return new PriceBar(start.AddDays(i * step), close, close + 1m, close - 1m, close, 1_000 + i);
        }).ToList();
        var equity = bars.Select((bar, i) => new EquityPoint(
            bar.Date, 10_000m + i * 10m, 10_000m - i * 5m, 0.01 * (i % 3), 0.02 * (i % 4), i % 2 == 0)).ToList();
        var trades = Enumerable.Range(0, tradeCount).Select(i => new Trade(
            bars[Math.Min(i % barCount, barCount - 1)].Date,
            100.5m + i,
            bars[Math.Min((i % barCount) + 1, barCount - 1)].Date,
            101.25m + i,
            99.123456m,
            1.99m,
            i % 2 == 0 ? 72.15m : -10.5m,
            i % 2 == 0 ? 0.0072 : -0.00105,
            (ExitReason)(i % 5),
            i % 7)).ToList();

        var request = new BacktestRequest(
            "MSFT",
            timeframe,
            "sma-crossover",
            parameters ?? new Dictionary<string, double> { ["slow"] = 50, ["fast"] = 20 },
            null,
            null,
            execution ?? new ExecutionSettings(StopLossFraction: 0.05m, TrailingStopFraction: 0.1m, RiskFreeRate: 0.02));

        return new BacktestResult(
            request,
            "Moving average crossover",
            pricesAdjusted,
            bars,
            equity,
            trades,
            StrategyMetrics with { TradeCount = tradeCount },
            BenchmarkMetrics,
            indicators ?? [Indicator(barCount, "sma-20", "SMA 20", IndicatorPane.Price), Indicator(barCount, "rsi-14", "RSI 14", IndicatorPane.Oscillator, 30, 70)]);
    }

    public static IndicatorSeries Indicator(int barCount, string key, string label, IndicatorPane pane, params double[] referenceLines)
        => new(key, label, pane, Enumerable.Range(0, barCount).Select(i => i < 5 ? (double?)null : 100 + Math.Sin(i)).ToList(), referenceLines);
}
