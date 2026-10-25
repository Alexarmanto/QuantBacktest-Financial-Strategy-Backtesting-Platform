using System.Globalization;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

internal sealed record MetricRow(string Label, string Strategy, string BuyAndHold);

internal sealed record SettingRow(string Label, string Value);

/// <summary>
/// Minimal result page (PR 2): main metrics, settings and permalink. Every value is already computed by
/// IBacktestService; this model only formats (DisplayFormat, P4-U14). The full page comes with P4-U3 to P4-U7.
/// </summary>
internal sealed record BacktestResultViewModel(
    string Symbol,
    string StrategyName,
    string Timeframe,
    string Period,
    IReadOnlyList<MetricRow> Metrics,
    IReadOnlyList<SettingRow> Execution,
    IReadOnlyList<SettingRow> Parameters,
    string Permalink)
{
    public const string Disabled = "Off";

    public static BacktestResultViewModel Create(BacktestResult result, IStrategy strategy, string permalink)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(permalink);

        var request = result.Request;
        var execution = request.Execution;
        var strategyMetrics = result.Metrics;
        var benchmark = result.BenchmarkMetrics;

        // Actual period: first and last bar of the tested period (BacktestResult.Bars).
        var period = result.Bars.Count == 0
            ? DisplayFormat.NotAvailable
            : $"{FormatDate(result.Bars[0].Date)} to {FormatDate(result.Bars[^1].Date)}";

        var metrics = new List<MetricRow>
        {
            new("Final equity", DisplayFormat.Money(strategyMetrics.FinalEquity), DisplayFormat.Money(benchmark.FinalEquity)),
            new("Cumulative return", DisplayFormat.Percent(strategyMetrics.CumulativeReturn), DisplayFormat.Percent(benchmark.CumulativeReturn)),
            new("Annualized return", DisplayFormat.Percent(strategyMetrics.AnnualizedReturn), DisplayFormat.Percent(benchmark.AnnualizedReturn)),
            new("Sharpe ratio", DisplayFormat.Ratio(strategyMetrics.SharpeRatio), DisplayFormat.Ratio(benchmark.SharpeRatio)),
            new("Max drawdown", DisplayFormat.Percent(strategyMetrics.MaxDrawdown), DisplayFormat.Percent(benchmark.MaxDrawdown)),
            new("Trades", FormatCount(strategyMetrics.TradeCount), FormatCount(benchmark.TradeCount)),
            new("Win rate", DisplayFormat.Percent(strategyMetrics.WinRate), DisplayFormat.Percent(benchmark.WinRate)),
        };

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

        return new BacktestResultViewModel(
            request.Symbol,
            result.StrategyName,
            request.Timeframe.ToString(),
            period,
            metrics,
            settings,
            parameters,
            permalink);
    }

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);

    private static string FormatCount(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static string FormatOptional(decimal? fraction) => fraction is { } value ? DisplayFormat.Percent((double)value) : Disabled;
}
