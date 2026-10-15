using QuantBacktest.Core.Models;

namespace QuantBacktest.Reports.Pdf;

/// <summary>One line of the "Strategy vs Buy &amp; hold" table.</summary>
internal sealed record MetricRow(string Label, string Strategy, string Benchmark);

/// <summary>Texts printed in the report, built from the result as it is (P1-R2: no metric is recomputed).</summary>
internal static class ReportContent
{
    public const string Title = "QuantBacktest report";
    public const string UnadjustedWarning = "Prices are not adjusted for splits or dividends.";
    public const string NoTrades = "No trades were executed in this period.";

    /// <summary>P1-R4: every field of Trade, exit reason included.</summary>
    public static readonly IReadOnlyList<string> TradeColumns =
        ["#", "Entry date", "Entry price", "Exit date", "Exit price", "Quantity", "Fees", "Net profit", "Return", "Exit reason", "Bars held"];

    /// <summary>P1-R5, with the date of the injected clock.</summary>
    public static string Footer(DateTimeOffset generatedAt)
        => $"Generated on {generatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", ReportFormat.Culture)} UTC by QuantBacktest — Market data: Alpha Vantage";

    /// <summary>P1-R2: the backtest settings shown at the top of page 1.</summary>
    public static IReadOnlyList<(string Label, string Value)> Summary(BacktestResult result)
    {
        var request = result.Request;
        var execution = request.Execution;
        var period = result.Bars.Count == 0
            ? ReportFormat.NotAvailable
            : $"{ReportFormat.Date(result.Bars[0].Date)} to {ReportFormat.Date(result.Bars[^1].Date)}";

        return
        [
            ("Symbol", request.Symbol),
            ("Strategy", $"{result.StrategyName} ({request.StrategyId})"),
            ("Parameters", Parameters(request.Parameters)),
            ("Period", period),
            ("Timeframe", ReportFormat.Timeframe(request.Timeframe)),
            ("Initial capital", ReportFormat.Money(execution.InitialCapital)),
            ("Fees", ReportFormat.Percent(execution.FeeRate) + " per fill"),
            ("Slippage", ReportFormat.Percent(execution.SlippageRate)),
            ("Stops", Stops(execution)),
            ("Risk-free rate", ReportFormat.Percent(execution.RiskFreeRate) + " per year"),
        ];
    }

    /// <summary>P1-R2: the 13 fields of PerformanceMetrics, strategy then benchmark; null ratios read "n/a".</summary>
    public static IReadOnlyList<MetricRow> Metrics(BacktestResult result)
    {
        var s = result.Metrics;
        var b = result.BenchmarkMetrics;
        var averageLabel = result.Request.Timeframe == Timeframe.Weekly ? "Average weekly return" : "Average daily return";

        return
        [
            new("Final equity", ReportFormat.Money(s.FinalEquity), ReportFormat.Money(b.FinalEquity)),
            new("Cumulative return", ReportFormat.SignedPercent(s.CumulativeReturn), ReportFormat.SignedPercent(b.CumulativeReturn)),
            new("Annualized return", ReportFormat.SignedPercent(s.AnnualizedReturn), ReportFormat.SignedPercent(b.AnnualizedReturn)),
            new(averageLabel, ReportFormat.SignedPercent(s.AverageReturnPerPeriod), ReportFormat.SignedPercent(b.AverageReturnPerPeriod)),
            new("Annualized volatility", ReportFormat.Percent(s.AnnualizedVolatility), ReportFormat.Percent(b.AnnualizedVolatility)),
            new("Sharpe ratio", ReportFormat.Ratio(s.SharpeRatio), ReportFormat.Ratio(b.SharpeRatio)),
            new("Sortino ratio", ReportFormat.Ratio(s.SortinoRatio), ReportFormat.Ratio(b.SortinoRatio)),
            new("Calmar ratio", ReportFormat.Ratio(s.CalmarRatio), ReportFormat.Ratio(b.CalmarRatio)),
            new("Max drawdown", ReportFormat.Drawdown(s.MaxDrawdown), ReportFormat.Drawdown(b.MaxDrawdown)),
            new("Trades", ReportFormat.Integer(s.TradeCount), ReportFormat.Integer(b.TradeCount)),
            new("Win rate", ReportFormat.Percent(s.WinRate), ReportFormat.Percent(b.WinRate)),
            new("Profit factor", ReportFormat.Ratio(s.ProfitFactor), ReportFormat.Ratio(b.ProfitFactor)),
            new("Exposure", ReportFormat.Percent(s.Exposure), ReportFormat.Percent(b.Exposure)),
        ];
    }

    /// <summary>P1-R4: one row per trade, in the order of the result, values as they are.</summary>
    public static IReadOnlyList<string[]> Trades(BacktestResult result)
        => [.. result.Trades.Select((trade, index) => new[]
        {
            ReportFormat.Integer(index + 1),
            ReportFormat.Date(trade.EntryDate),
            ReportFormat.Price(trade.EntryPrice),
            ReportFormat.Date(trade.ExitDate),
            ReportFormat.Price(trade.ExitPrice),
            ReportFormat.Quantity(trade.Quantity),
            ReportFormat.Money(trade.Fees),
            ReportFormat.Money(trade.NetProfit),
            ReportFormat.SignedPercent(trade.ReturnFraction),
            ExitReasonLabel(trade.ExitReason),
            ReportFormat.Integer(trade.HoldingBars),
        })];

    public static string ExitReasonLabel(ExitReason reason) => reason switch
    {
        ExitReason.Signal => "Signal",
        ExitReason.StopLoss => "Stop-loss",
        ExitReason.TakeProfit => "Take-profit",
        ExitReason.TrailingStop => "Trailing stop",
        ExitReason.EndOfData => "End of data",
        _ => reason.ToString(),
    };

    private static string Parameters(IReadOnlyDictionary<string, double> parameters)
        => parameters.Count == 0
            ? "Strategy defaults"
            : string.Join(", ", parameters
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} = {ReportFormat.ParameterValue(pair.Value)}"));

    private static string Stops(ExecutionSettings execution)
    {
        var stops = new List<string>();
        if (execution.StopLossFraction is { } stopLoss)
        {
            stops.Add($"stop-loss {ReportFormat.Percent(stopLoss)}");
        }

        if (execution.TakeProfitFraction is { } takeProfit)
        {
            stops.Add($"take-profit {ReportFormat.Percent(takeProfit)}");
        }

        if (execution.TrailingStopFraction is { } trailing)
        {
            stops.Add($"trailing stop {ReportFormat.Percent(trailing)}");
        }

        return stops.Count == 0 ? "None" : string.Join(", ", stops);
    }
}
