using System.Text.Json;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>One bin of a histogram, as text: its range, its count (from Core) and whether it holds the actual value.</summary>
internal sealed record HistogramRow(string Range, string Count, bool ContainsActual);

/// <summary>
/// What monte-carlo-charts.js draws for one histogram: one entry per bin of Core, in Core's order. Ticks are the lower
/// bounds of the bins (the X axis title says so); Ranges, "Lower to Upper", go in the tooltip.
/// </summary>
internal sealed record HistogramChartData(
    string CanvasId,
    string XAxisTitle,
    IReadOnlyList<string> Ranges,
    IReadOnlyList<string> Ticks,
    IReadOnlyList<int> Counts,
    int? ActualIndex,
    string ActualText);

internal sealed record MonteCarloChartsPayload(
    string YAxisTitle,
    string CountLabel,
    string ActualLabel,
    HistogramChartData FinalReturn,
    HistogramChartData MaxDrawdown);

/// <summary>
/// One histogram of the page: canvas id, aria-label (Summary), the reading sentence shown under the chart, and the
/// table of its bins (also read without JavaScript).
/// </summary>
internal sealed record MonteCarloHistogramViewModel(
    string Title,
    string CanvasId,
    string Summary,
    string Reading,
    IReadOnlyList<HistogramRow> Rows);

/// <summary>
/// The two histograms of the Monte Carlo page (P4-X4, MC6). Bins and counts are those of IMonteCarloService: nothing is
/// counted, binned or averaged here. The only extra step is presentation: finding, by comparing with the bounds given
/// by Core, the bin that holds the actual value, so the chart can highlight it. Drawdowns stay positive, as in the
/// Performance table of the result page (BacktestResultViewModel: DisplayFormat.Percent(MaxDrawdown)).
/// </summary>
internal sealed record MonteCarloChartsViewModel(
    string Json,
    MonteCarloHistogramViewModel FinalReturn,
    MonteCarloHistogramViewModel MaxDrawdown)
{
    /// <summary>Id of the page's single &lt;script type="application/json"&gt; (P4-U16).</summary>
    public const string DataId = "monte-carlo-data";

    public const string FinalReturnCanvasId = "chart-mc-final-return";
    public const string MaxDrawdownCanvasId = "chart-mc-max-drawdown";

    /// <summary>Same labels as the columns of the percentile table.</summary>
    public const string FinalReturnTitle = "Final return";
    public const string MaxDrawdownTitle = "Max drawdown (trade by trade)";

    public const string YAxisTitle = "Number of simulations";
    public const string CountLabel = "Simulations";
    public const string ActualLabel = "Range containing the actual result";

    /// <summary>Appended to the X axis title: the ticks are the lower bounds of the bins.</summary>
    public const string LowerBoundNote = "lower bound of each range";

    /// <summary>
    /// Name of the highlight colour in the reading sentence: --qb-series-1 of site.css, used by monte-carlo-charts.js
    /// (theme.palette[0]) for the bin of the actual value. Change both together.
    /// </summary>
    public const string ActualColourName = "Orange";

    /// <summary>Same options as the result charts: camelCase and the default encoder (escapes &lt; &gt; &amp;).</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static MonteCarloChartsViewModel Create(MonteCarloResult monteCarlo)
    {
        ArgumentNullException.ThrowIfNull(monteCarlo);

        var simulations = MonteCarloViewModel.FormatCount(monteCarlo.Request.Simulations);
        var finalReturn = Histogram(
            FinalReturnTitle, FinalReturnCanvasId, "final return", simulations, monteCarlo.FinalReturnHistogram, monteCarlo.ActualFinalReturn);
        var maxDrawdown = Histogram(
            MaxDrawdownTitle, MaxDrawdownCanvasId, "max drawdown (trade by trade)", simulations, monteCarlo.MaxDrawdownHistogram, monteCarlo.ActualMaxDrawdown);

        var payload = new MonteCarloChartsPayload(YAxisTitle, CountLabel, ActualLabel, finalReturn.Data, maxDrawdown.Data);
        return new MonteCarloChartsViewModel(JsonSerializer.Serialize(payload, JsonOptions), finalReturn.View, maxDrawdown.View);
    }

    /// <summary>
    /// Index of the bin that holds value, with the bin semantics of MC6 / HistogramBin: [Lower, Upper), the last bin
    /// also includes Upper (so a single bin [min, min] holds min). Null when value is not finite or lies outside
    /// every bin: the chart then highlights nothing and the page gives the value as text only.
    /// </summary>
    public static int? ActualBinIndex(IReadOnlyList<HistogramBin> bins, double value)
    {
        ArgumentNullException.ThrowIfNull(bins);
        if (!double.IsFinite(value))
        {
            return null;
        }

        for (var i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            var isLast = i == bins.Count - 1;
            if (value >= bin.Lower && (value < bin.Upper || (isLast && value <= bin.Upper)))
            {
                return i;
            }
        }

        return null;
    }

    private static (HistogramChartData Data, MonteCarloHistogramViewModel View) Histogram(
        string title,
        string canvasId,
        string noun,
        string simulations,
        IReadOnlyList<HistogramBin> bins,
        double actual)
    {
        var actualIndex = ActualBinIndex(bins, actual);
        var ranges = bins.Select(bin => $"{MonteCarloViewModel.Percent(bin.Lower)} to {MonteCarloViewModel.Percent(bin.Upper)}").ToList();
        var actualValue = MonteCarloViewModel.Percent(actual);

        // Tooltip of the highlighted bin, and end of the aria-label.
        var actualText = actualIndex is { } index
            ? $"Actual value (real trade order): {actualValue}, in the range {ranges[index]}."
            : $"Actual value (real trade order): {actualValue}, outside the simulated range.";

        // Sentence under the chart (decision 1d): how to read a bar, then where the actual result is.
        var reading = $"Each bar counts the simulations whose {noun} falls in that range. "
            + (actualIndex is not null
                ? $"{ActualColourName}: the range containing the actual result ({actualValue})."
                : actualText);

        var range = bins.Count == 0
            ? DisplayFormat.NotAvailable
            : $"{MonteCarloViewModel.Percent(bins[0].Lower)} to {MonteCarloViewModel.Percent(bins[^1].Upper)}";
        var rangeCount = MonteCarloViewModel.FormatCount(bins.Count);
        var summary = $"Histogram: number of simulations by {noun} range, {simulations} simulations in {rangeCount} "
            + $"range{(bins.Count == 1 ? string.Empty : "s")} from {range}. {actualText}";

        var rows = bins
            .Select((bin, i) => new HistogramRow(ranges[i], MonteCarloViewModel.FormatCount(bin.Count), i == actualIndex))
            .ToList();

        var data = new HistogramChartData(
            canvasId,
            $"{title}, {LowerBoundNote}",
            ranges,
            bins.Select(bin => MonteCarloViewModel.Percent(bin.Lower)).ToList(),
            bins.Select(bin => bin.Count).ToList(),
            actualIndex,
            actualText);

        return (data, new MonteCarloHistogramViewModel(title, canvasId, summary, reading, rows));
    }
}
