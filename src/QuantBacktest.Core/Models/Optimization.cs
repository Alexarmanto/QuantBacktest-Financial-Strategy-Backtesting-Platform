namespace QuantBacktest.Core.Models;

/// <summary>Metric optimised by a heatmap. Higher is always better.</summary>
public enum SweepMetric
{
    SharpeRatio = 0,
    SortinoRatio = 1,
    CalmarRatio = 2,
    CumulativeReturn = 3,
    AnnualizedReturn = 4,
    ProfitFactor = 5,
}

/// <summary>Values tested for one parameter: Min, Min + Step, ... up to Max inclusive (docs/CONTRACT.md section 6).</summary>
public sealed record SweepAxis(string ParameterKey, double Min, double Max, double Step);

/// <summary>A heatmap request. BaseRequest gives everything else (symbol, period, other parameters, execution).</summary>
public sealed record SweepRequest(BacktestRequest BaseRequest, SweepAxis X, SweepAxis Y, SweepMetric Metric);

/// <summary>
/// One heatmap cell. Value is null when the metric is undefined or the combination is invalid;
/// Error then explains why (English, displayable), otherwise Error is null.
/// </summary>
public sealed record SweepCell(double X, double Y, double? Value, int TradeCount, string? Error);

/// <summary>
/// Cells are in row-major order: for each value of YValues, every value of XValues.
/// Best is the cell with the highest non-null Value (ties: smallest Y, then smallest X); null if none.
/// IsIsolatedPeak is the overfitting warning (rule O4 of docs/CONTRACT.md).
/// </summary>
public sealed record SweepResult(
    SweepRequest Request,
    IReadOnlyList<double> XValues,
    IReadOnlyList<double> YValues,
    IReadOnlyList<SweepCell> Cells,
    SweepCell? Best,
    double? BestNeighborhoodAverage,
    bool IsIsolatedPeak);
