using System.Globalization;
using System.Text.Json;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>One equity curve of the comparator; Color is the 1-based index of the palette (--qb-compare-N).</summary>
internal sealed record CompareSeries(string Label, int Color, IReadOnlyList<double> Values);

/// <summary>Everything compare-charts.js draws, one entry per date of Dates.</summary>
internal sealed record CompareChartsPayload(
    IReadOnlyList<string> Dates,
    string BenchmarkLabel,
    IReadOnlyList<double> Benchmark,
    IReadOnlyList<CompareSeries> Series);

/// <summary>
/// The chart of the comparator (P4-X1, C1): one equity curve per successful strategy, in the order of the URL, and a
/// single buy &amp; hold benchmark. Dates and benchmark come from the first successful result (BacktestResult.Equity):
/// the curves are only copied, never computed. Json goes into the page's single &lt;script type="application/json"&gt;.
/// </summary>
internal sealed record CompareChartsViewModel(string Json, string Summary)
{
    /// <param name="results">The successful results, in the order of the URL; at least one.</param>
    /// <exception cref="InvalidOperationException">
    /// A result has an equity curve with other dates than the first one: docs/CONTRACT.md section 3 ("Equity a
    /// exactement un point par barre de Bars") and X1 make them equal for the same symbol, timeframe and period.
    /// </exception>
    public static CompareChartsViewModel Create(IReadOnlyList<BacktestResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (results.Count == 0)
        {
            throw new ArgumentException("At least one successful result is needed to draw the comparison.", nameof(results));
        }

        var reference = results[0].Equity;
        foreach (var result in results.Skip(1))
        {
            EnsureSameDates(reference, result);
        }

        var dates = reference.Select(point => FormatDate(point.Date)).ToList();

        // EquityPoint.StrategyEquity and BenchmarkEquity are decimals: their double value is always finite.
        var series = results
            .Select((result, index) => new CompareSeries(
                result.StrategyName,
                index + 1,
                result.Equity.Select(point => (double)point.StrategyEquity).ToList()))
            .ToList();
        var benchmark = reference.Select(point => (double)point.BenchmarkEquity).ToList();

        var payload = new CompareChartsPayload(dates, BacktestResultViewModel.BenchmarkColumn, benchmark, series);
        var finals = string.Join(", ", results.Select(r => $"{r.StrategyName} {DisplayFormat.Money(r.Metrics.FinalEquity)}"));
        var summary = $"Equity of {Count(results.Count, "strategy", "strategies")} and of "
            + $"{BacktestResultViewModel.BenchmarkColumn.ToLowerInvariant()} from {dates[0]} to {dates[^1]}. "
            + $"Final equity: {finals}, {BacktestResultViewModel.BenchmarkColumn.ToLowerInvariant()} "
            + $"{DisplayFormat.Money(results[0].BenchmarkMetrics.FinalEquity)}.";

        return new CompareChartsViewModel(JsonSerializer.Serialize(payload, ResultChartsViewModel.JsonOptions), summary);
    }

    private static void EnsureSameDates(IReadOnlyList<EquityPoint> reference, BacktestResult result)
    {
        if (result.Equity.Count != reference.Count)
        {
            throw new InvalidOperationException(
                $"The equity of '{result.StrategyName}' has {result.Equity.Count} points instead of {reference.Count}: "
                + "the results of one comparison must cover the same bars (docs/CONTRACT.md section 3).");
        }

        for (var i = 0; i < reference.Count; i++)
        {
            if (result.Equity[i].Date != reference[i].Date)
            {
                throw new InvalidOperationException(
                    $"The equity of '{result.StrategyName}' is dated {FormatDate(result.Equity[i].Date)} at index {i}, "
                    + $"not {FormatDate(reference[i].Date)}: the results of one comparison must cover the same bars "
                    + "(docs/CONTRACT.md section 3).");
            }
        }
    }

    private static string Count(int count, string singular, string plural) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
}
