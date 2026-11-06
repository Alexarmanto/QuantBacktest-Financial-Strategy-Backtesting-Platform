using System.Globalization;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One field of PerformanceMetrics as shown in a metrics table: its label, its display (P4-U14), the value whose sign
/// colours it (returns only, P4-U4; null for the uncoloured lines) and the heatmap metric it is, if any.
/// Only reads and formats a field: nothing is computed.
/// </summary>
internal sealed record MetricDefinition(
    string Label,
    Func<PerformanceMetrics, string> Format,
    Func<PerformanceMetrics, double>? ToneValue = null,
    SweepMetric? Sweep = null)
{
    /// <summary>The tone class of this line for one result (ValueTone.Neutral on uncoloured lines).</summary>
    public string Tone(PerformanceMetrics metrics) =>
        ToneValue is { } value ? ValueTone.Of(value(metrics)) : ValueTone.Neutral;
}

/// <summary>
/// The 13 fields of PerformanceMetrics, in the order of the record (M1 to M12), defined once: the Strategy vs Buy &amp;
/// hold table of the result page (P4-U4) and the train vs test table (P4-X3) print the same lines.
/// </summary>
internal static class MetricDefinitions
{
    public static IReadOnlyList<MetricDefinition> For(Timeframe timeframe) =>
    [
        new("Final equity", m => DisplayFormat.Money(m.FinalEquity)),
        Return("Cumulative return", m => m.CumulativeReturn, SweepMetric.CumulativeReturn),
        Return("Annualized return", m => m.AnnualizedReturn, SweepMetric.AnnualizedReturn),
        Return(BacktestResultViewModel.AverageReturnLabel(timeframe), m => m.AverageReturnPerPeriod),
        new("Annualized volatility", m => DisplayFormat.Percent(m.AnnualizedVolatility)),
        new("Sharpe ratio", m => DisplayFormat.Ratio(m.SharpeRatio), Sweep: SweepMetric.SharpeRatio),
        new("Sortino ratio", m => DisplayFormat.Ratio(m.SortinoRatio), Sweep: SweepMetric.SortinoRatio),
        new("Calmar ratio", m => DisplayFormat.Ratio(m.CalmarRatio), Sweep: SweepMetric.CalmarRatio),
        new("Max drawdown", m => DisplayFormat.Percent(m.MaxDrawdown)),
        new("Trades", m => m.TradeCount.ToString(CultureInfo.InvariantCulture)),
        new("Win rate", m => DisplayFormat.Percent(m.WinRate)),
        new("Profit factor", m => DisplayFormat.Ratio(m.ProfitFactor), Sweep: SweepMetric.ProfitFactor),
        new("Exposure", m => DisplayFormat.Percent(m.Exposure)),
    ];

    private static MetricDefinition Return(string label, Func<PerformanceMetrics, double> value, SweepMetric? sweep = null) =>
        new(label, m => DisplayFormat.Percent(value(m)), value, sweep);
}
