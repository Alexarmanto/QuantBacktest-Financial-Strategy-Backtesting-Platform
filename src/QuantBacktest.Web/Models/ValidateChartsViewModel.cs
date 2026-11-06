using System.Globalization;
using System.Text.Json;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>One equity chart of the train/test page: the curves of BacktestResult.Equity, one value per date.</summary>
internal sealed record ValidateEquityPayload(
    string CanvasId,
    IReadOnlyList<string> Dates,
    IReadOnlyList<double> Strategy,
    IReadOnlyList<double> Benchmark);

/// <summary>
/// Everything validate-charts.js draws, in the page's single &lt;script type="application/json"&gt; (P4-U16): the
/// training heatmap, exactly the payload heatmap.js reads on the heatmap page (HeatmapViewModel.Payload, whose
/// non-finite values are already null), and one equity chart per period (empty without a best cell). The curves are
/// copied from the two results (decimals, always finite), never computed.
/// </summary>
internal sealed record ValidateChartsViewModel(string Json, string? TrainSummary, string? TestSummary)
{
    public const string DataId = "validate-charts-data";
    public const string TrainCanvasId = "chart-equity-train";
    public const string TestCanvasId = "chart-equity-test";

    public static ValidateChartsViewModel Create(HeatmapViewModel heatmap, BacktestResult? train, BacktestResult? test)
    {
        ArgumentNullException.ThrowIfNull(heatmap);
        var periods = new List<ValidateEquityPayload>();
        if (train is not null)
        {
            periods.Add(Period(TrainCanvasId, train));
        }

        if (test is not null)
        {
            periods.Add(Period(TestCanvasId, test));
        }

        var payload = new
        {
            heatmap = heatmap.Payload,
            strategyLabel = BacktestResultViewModel.StrategyColumn,
            benchmarkLabel = BacktestResultViewModel.BenchmarkColumn,
            periods,
        };

        return new ValidateChartsViewModel(
            JsonSerializer.Serialize(payload, ResultChartsViewModel.JsonOptions),
            train is null ? null : Summary("training", train),
            test is null ? null : Summary("test", test));
    }

    private static ValidateEquityPayload Period(string canvasId, BacktestResult result) => new(
        canvasId,
        result.Equity.Select(point => FormatDate(point.Date)).ToList(),
        result.Equity.Select(point => (double)point.StrategyEquity).ToList(),
        result.Equity.Select(point => (double)point.BenchmarkEquity).ToList());

    private static string Summary(string period, BacktestResult result)
    {
        var benchmark = BacktestResultViewModel.BenchmarkColumn.ToLowerInvariant();
        var dates = result.Equity.Count == 0
            ? string.Empty
            : $", from {FormatDate(result.Equity[0].Date)} to {FormatDate(result.Equity[^1].Date)}";
        return $"Equity of the strategy and of {benchmark} on the {period} period{dates}. "
            + $"Final equity: strategy {DisplayFormat.Money(result.Metrics.FinalEquity)}, "
            + $"{benchmark} {DisplayFormat.Money(result.BenchmarkMetrics.FinalEquity)}.";
    }

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
}
