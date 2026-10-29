using System.Globalization;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>One line of the Strategy vs Buy &amp; hold table; the tone classes are empty on uncoloured lines.</summary>
internal sealed record MetricRow(
    string Label,
    string Strategy,
    string BuyAndHold,
    string StrategyTone = ValueTone.Neutral,
    string BuyAndHoldTone = ValueTone.Neutral);

/// <summary>
/// Result page (P4-U3 to P4-U7). Every value is already computed by IBacktestService: this model only
/// formats (DisplayFormat, P4-U14) and picks a colour from the sign of a value. The view only prints it.
/// </summary>
internal sealed record BacktestResultViewModel(
    ResultHeaderViewModel Header,
    IReadOnlyList<MetricRow> Metrics,
    ResultChartsViewModel Charts,
    ResultTradesViewModel Trades,
    ResultActionsViewModel Actions)
{
    public const string Disabled = "Off";

    /// <summary>Column headers of the metrics table, reused as the legend of the charts (P4-U5).</summary>
    public const string StrategyColumn = "Strategy";
    public const string BenchmarkColumn = "Buy & hold";

    /// <param name="query">The effective (canonical) query of this result: it builds every action URL.</param>
    /// <param name="permalink">Absolute URL of this result, copied by "Copy link".</param>
    /// <param name="pathBase">Request.PathBase, prefixed to the action URLs.</param>
    public static BacktestResultViewModel Create(
        BacktestResult result,
        IStrategy strategy,
        BacktestQuery query,
        string permalink,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(permalink);

        return new BacktestResultViewModel(
            CreateHeader(result, strategy, permalink),
            CreateMetricRows(result.Metrics, result.BenchmarkMetrics, result.Request.Timeframe),
            ResultChartsViewModel.Create(result),
            ResultTradesViewModel.Create(result.Trades),
            ResultActionsViewModel.Create(query, strategy, pathBase));
    }

    /// <summary>M4: the label of AverageReturnPerPeriod follows the timeframe.</summary>
    public static string AverageReturnLabel(Timeframe timeframe) => timeframe switch
    {
        Timeframe.Daily => "Average daily return",
        Timeframe.Weekly => "Average weekly return",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };

    private static ResultHeaderViewModel CreateHeader(BacktestResult result, IStrategy strategy, string permalink)
    {
        var request = result.Request;
        var execution = request.Execution;

        // Actual period: first and last bar of the tested period (BacktestResult.Bars).
        var period = result.Bars.Count == 0
            ? DisplayFormat.NotAvailable
            : $"{FormatDate(result.Bars[0].Date)} to {FormatDate(result.Bars[^1].Date)}";

        var settings = new List<SettingRow>
        {
            new("Initial capital", DisplayFormat.Money(execution.InitialCapital)),
            new("Fee", DisplayFormat.Percent((double)execution.FeeRate)),
            new("Slippage", DisplayFormat.Percent((double)execution.SlippageRate)),
            new("Stop-loss", FormatOptional(execution.StopLossFraction)),
            new("Take-profit", FormatOptional(execution.TakeProfitFraction)),
            new("Trailing stop", FormatOptional(execution.TrailingStopFraction)),
            new("Risk-free rate", DisplayFormat.Percent(execution.RiskFreeRate)),
        };

        // Values sent with the request; a key the request did not carry runs with its catalog default.
        var parameters = strategy.Parameters
            .Select(definition => new SettingRow(
                definition.Label,
                BacktestQuery.FormatNumber(request.Parameters.TryGetValue(definition.Key, out var value) ? value : definition.DefaultValue)))
            .ToList();

        return new ResultHeaderViewModel(
            request.Symbol,
            result.StrategyName,
            request.Timeframe.ToString(),
            period,
            settings,
            parameters,
            permalink,
            result.PricesAdjusted ? null : ResultHeaderViewModel.UnadjustedPricesWarning,
            strategy.Documentation);
    }

    /// <summary>The 13 fields of PerformanceMetrics, in the order of the record (M1 to M12). Only returns are coloured.</summary>
    private static List<MetricRow> CreateMetricRows(PerformanceMetrics strategy, PerformanceMetrics benchmark, Timeframe timeframe) =>
    [
        new("Final equity", DisplayFormat.Money(strategy.FinalEquity), DisplayFormat.Money(benchmark.FinalEquity)),
        Return("Cumulative return", strategy.CumulativeReturn, benchmark.CumulativeReturn),
        Return("Annualized return", strategy.AnnualizedReturn, benchmark.AnnualizedReturn),
        Return(AverageReturnLabel(timeframe), strategy.AverageReturnPerPeriod, benchmark.AverageReturnPerPeriod),
        new("Annualized volatility", DisplayFormat.Percent(strategy.AnnualizedVolatility), DisplayFormat.Percent(benchmark.AnnualizedVolatility)),
        new("Sharpe ratio", DisplayFormat.Ratio(strategy.SharpeRatio), DisplayFormat.Ratio(benchmark.SharpeRatio)),
        new("Sortino ratio", DisplayFormat.Ratio(strategy.SortinoRatio), DisplayFormat.Ratio(benchmark.SortinoRatio)),
        new("Calmar ratio", DisplayFormat.Ratio(strategy.CalmarRatio), DisplayFormat.Ratio(benchmark.CalmarRatio)),
        new("Max drawdown", DisplayFormat.Percent(strategy.MaxDrawdown), DisplayFormat.Percent(benchmark.MaxDrawdown)),
        new("Trades", FormatCount(strategy.TradeCount), FormatCount(benchmark.TradeCount)),
        new("Win rate", DisplayFormat.Percent(strategy.WinRate), DisplayFormat.Percent(benchmark.WinRate)),
        new("Profit factor", DisplayFormat.Ratio(strategy.ProfitFactor), DisplayFormat.Ratio(benchmark.ProfitFactor)),
        new("Exposure", DisplayFormat.Percent(strategy.Exposure), DisplayFormat.Percent(benchmark.Exposure)),
    ];

    private static MetricRow Return(string label, double strategy, double benchmark) =>
        new(label, DisplayFormat.Percent(strategy), DisplayFormat.Percent(benchmark), ValueTone.Of(strategy), ValueTone.Of(benchmark));

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);

    private static string FormatCount(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static string FormatOptional(decimal? fraction) => fraction is { } value ? DisplayFormat.Percent((double)value) : Disabled;
}
