using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine.Tests.TestSupport;

/// <summary>The 6-bar series of docs/parts/PART-3.md section 7, and small helpers to run the engine on it.</summary>
internal static class ReferenceSeries
{
    // six consecutive business days: Monday 2024-01-01 to Monday 2024-01-08
    public static readonly DateOnly[] Dates =
    {
        new(2024, 1, 1), new(2024, 1, 2), new(2024, 1, 3), new(2024, 1, 4), new(2024, 1, 5), new(2024, 1, 8),
    };

    private static readonly decimal[] Opens = { 100, 102, 104, 106, 103, 109 };
    private static readonly decimal[] Highs = { 102, 104, 106, 107, 108.5m, 111 };
    private static readonly decimal[] Lows = { 99, 101, 103, 103.5m, 102, 108 };
    private static readonly decimal[] Closes = { 101, 103, 105, 104, 108, 110 };

    public static PriceSeries Create()
    {
        List<PriceBar> bars = new();
        for (int i = 0; i < Dates.Length; i++)
        {
            bars.Add(new PriceBar(Dates[i], Opens[i], Highs[i], Lows[i], Closes[i], 1000));
        }

        return new PriceSeries("TEST", Timeframe.Daily, bars, IsAdjusted: true);
    }

    /// <summary>Capital 10 000 and fee 0.1 %, no slippage, no stops (the settings of the reference scenarios).</summary>
    public static ExecutionSettings Settings(
        decimal slippage = 0m,
        decimal? stopLoss = null,
        decimal? takeProfit = null,
        decimal? trailingStop = null,
        decimal fee = 0.001m)
    {
        return new ExecutionSettings(
            InitialCapital: 10_000m,
            FeeRate: fee,
            SlippageRate: slippage,
            StopLossFraction: stopLoss,
            TakeProfitFraction: takeProfit,
            TrailingStopFraction: trailingStop,
            RiskFreeRate: 0.0);
    }

    public static BacktestRequest Request(
        ExecutionSettings settings, DateOnly? from = null, DateOnly? to = null, string strategyId = "scripted")
    {
        return new BacktestRequest(
            "TEST", Timeframe.Daily, strategyId, new Dictionary<string, double>(), from, to, settings);
    }

    /// <summary>Runs the engine with a strategy written as letters ("BHSHHH").</summary>
    public static BacktestResult Run(
        string letters, ExecutionSettings? settings = null, DateOnly? from = null, DateOnly? to = null)
    {
        BacktestRequest request = Request(settings ?? Settings(), from, to);
        return new BacktestEngine().Run(request, Create(), ScriptedStrategy.FromLetters(letters));
    }
}
