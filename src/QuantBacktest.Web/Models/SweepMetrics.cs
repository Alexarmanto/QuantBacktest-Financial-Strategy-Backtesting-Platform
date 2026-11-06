using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>
/// Display of the heatmap metrics (P4-X2, P4-U14): English label, format (returns as percentages, ratios with 2
/// decimals, null as "n/a") and the centre of the diverging colour scale. Presentation only: the values come from
/// SweepCell.Value as returned by IParameterSweepService.
/// </summary>
internal static class SweepMetrics
{
    public const string BreakEven = "break-even";

    public static string Label(SweepMetric metric) => metric switch
    {
        SweepMetric.SharpeRatio => "Sharpe ratio",
        SweepMetric.SortinoRatio => "Sortino ratio",
        SweepMetric.CalmarRatio => "Calmar ratio",
        SweepMetric.CumulativeReturn => "Cumulative return",
        SweepMetric.AnnualizedReturn => "Annualized return",
        SweepMetric.ProfitFactor => "Profit factor",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown metric."),
    };

    /// <summary>
    /// The field of a result that a heatmap metric names (as Engine/ParameterSweepService reads it for a cell), so the
    /// train/test page can show the optimised metric of each period. Picks a field, never computes one.
    /// </summary>
    public static double? Read(SweepMetric metric, PerformanceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return metric switch
        {
            SweepMetric.SharpeRatio => metrics.SharpeRatio,
            SweepMetric.SortinoRatio => metrics.SortinoRatio,
            SweepMetric.CalmarRatio => metrics.CalmarRatio,
            SweepMetric.CumulativeReturn => metrics.CumulativeReturn,
            SweepMetric.AnnualizedReturn => metrics.AnnualizedReturn,
            SweepMetric.ProfitFactor => metrics.ProfitFactor,
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown metric."),
        };
    }

    public static bool IsReturn(SweepMetric metric) =>
        metric is SweepMetric.CumulativeReturn or SweepMetric.AnnualizedReturn;

    /// <summary>A value of the metric as shown in the tooltip, the legend and the table.</summary>
    public static string Format(SweepMetric metric, double? value)
    {
        if (value is not { } defined || !double.IsFinite(defined))
        {
            return DisplayFormat.NotAvailable;
        }

        return IsReturn(metric) ? DisplayFormat.Percent(defined) : DisplayFormat.Ratio(defined);
    }

    /// <summary>
    /// Centre of the diverging scale (decision g-5): 1 for the profit factor (gains equal losses), 0 for every other
    /// metric (Sharpe, Sortino, Calmar and returns change sign there).
    /// </summary>
    public static double Centre(SweepMetric metric) => metric == SweepMetric.ProfitFactor ? 1 : 0;

    /// <summary>Text of the centre of the legend: "0.00", "0.00%", or "1.00 (break-even)" for the profit factor.</summary>
    public static string CentreLabel(SweepMetric metric)
    {
        var text = Format(metric, Centre(metric));
        return metric == SweepMetric.ProfitFactor ? $"{text} ({BreakEven})" : text;
    }
}
