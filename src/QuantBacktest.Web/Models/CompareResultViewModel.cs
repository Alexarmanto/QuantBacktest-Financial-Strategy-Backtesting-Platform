using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>Header of a sortable column of the comparison table; SortType is read by table-sort.js.</summary>
internal sealed record CompareColumn(string Label, string SortType);

/// <summary>
/// One number cell of the comparison table: the formatted text (DisplayFormat, "n/a" for null), the raw invariant
/// sort key, and the gain / loss colour of a return. SortValue is null for an undefined ratio: the cell then has no
/// data-sort-value at all and table-sort.js keeps it last in both directions.
/// </summary>
/// <remarks>Built with a TagBuilder: Razor would write data-sort-value="" for a null value.</remarks>
internal sealed record MetricCell(string Text, string? SortValue, string Tone = ValueTone.Neutral)
{
    public IHtmlContent Td()
    {
        var cell = new TagBuilder("td");
        cell.AddCssClass(Tone.Length == 0 ? ResultTradesViewModel.NumberCell : $"{ResultTradesViewModel.NumberCell} {Tone}");
        if (SortValue is not null)
        {
            cell.Attributes["data-sort-value"] = SortValue;
        }

        cell.InnerHtml.Append(Text);
        return cell;
    }
}

/// <summary>
/// One line of the comparison table: a successful strategy (with the link to its detailed result) or the buy &amp;
/// hold benchmark (no link). SwatchClass is the colour of its curve on the chart.
/// </summary>
internal sealed record CompareRow(string Name, string? DetailUrl, string SwatchClass, IReadOnlyList<MetricCell> Cells);

/// <summary>A strategy that could not be run (C-5): its name and the message of its error, shown as received.</summary>
internal sealed record CompareError(string Name, string Message);

/// <summary>
/// The comparator result page (P4-X1, C1). Every value comes from the BacktestResult of each strategy: this model only
/// formats (DisplayFormat, P4-U14), picks the gain / loss colour of returns and builds the URLs (BacktestQuery).
/// </summary>
internal sealed record CompareResultViewModel(
    string Symbol,
    string Timeframe,
    string Period,
    IReadOnlyList<SettingRow> Execution,
    string Permalink,
    string EditSettingsUrl,
    IReadOnlyList<CompareError> Errors,
    IReadOnlyList<CompareRow> Rows,
    CompareChartsViewModel? Charts)
{
    public const string NothingRunMessage = "No strategy could be run.";
    public const string StrategyColumn = "Strategy";
    public const string SwatchClassPrefix = "qb-swatch qb-swatch-";
    public const string BenchmarkSwatchClass = "qb-swatch qb-swatch-benchmark";

    public const string FormPath = "/Compare";
    public const string ResultPath = "/Compare/Result";

    /// <summary>The columns of C-4, in this order; the first one is the strategy name.</summary>
    public static IReadOnlyList<CompareColumn> Columns { get; } =
    [
        new(StrategyColumn, ResultTradesViewModel.TextSort),
        new("Cumulative return", ResultTradesViewModel.NumberSort),
        new("Annualized return", ResultTradesViewModel.NumberSort),
        new("Annualized volatility", ResultTradesViewModel.NumberSort),
        new("Sharpe ratio", ResultTradesViewModel.NumberSort),
        new("Sortino ratio", ResultTradesViewModel.NumberSort),
        new("Calmar ratio", ResultTradesViewModel.NumberSort),
        new("Max drawdown", ResultTradesViewModel.NumberSort),
        new("Trades", ResultTradesViewModel.NumberSort),
        new("Profit factor", ResultTradesViewModel.NumberSort),
        new("Exposure", ResultTradesViewModel.NumberSort),
    ];

    public bool NothingRun => Rows.Count == 0;

    /// <param name="query">The canonical comparison query of this page.</param>
    /// <param name="outcomes">One per strategy, in the order of the URL.</param>
    /// <param name="permalink">Absolute URL of this page, copied by "Copy link".</param>
    /// <param name="pathBase">Request.PathBase, prefixed to the links.</param>
    public static CompareResultViewModel Create(
        ComparisonQuery query,
        IReadOnlyList<ComparisonOutcome> outcomes,
        string permalink,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(permalink);
        ArgumentNullException.ThrowIfNull(pathBase);

        var successes = outcomes.Where(o => o.Succeeded).ToList();
        var errors = outcomes.Where(o => !o.Succeeded).Select(o => new CompareError(o.StrategyName, o.Error!)).ToList();

        var rows = successes
            .Select((outcome, index) => new CompareRow(
                outcome.Result!.StrategyName,
                pathBase + ResultActionsViewModel.ResultPath + "?" + outcome.Query!.ToQueryString(),
                SwatchClassPrefix + (index + 1).ToString(CultureInfo.InvariantCulture),
                Cells(outcome.Result.Metrics)))
            .ToList();

        CompareChartsViewModel? charts = null;
        var period = DisplayFormat.NotAvailable;
        if (successes.Count > 0)
        {
            // The single benchmark (C1): the BenchmarkMetrics of the result its curve comes from (the first success).
            var reference = successes[0].Result!;
            rows.Add(new CompareRow(BacktestResultViewModel.BenchmarkColumn, null, BenchmarkSwatchClass, Cells(reference.BenchmarkMetrics)));
            charts = CompareChartsViewModel.Create(successes.Select(o => o.Result!).ToList());
            period = $"{FormatDate(reference.Equity[0].Date)} to {FormatDate(reference.Equity[^1].Date)}";
        }

        return new CompareResultViewModel(
            query.Symbol,
            query.Timeframe.ToString(),
            period,
            ResultHeaderViewModel.ExecutionRows(query.Execution),
            permalink,
            pathBase + FormPath + "?" + query.ToQueryString(),
            errors,
            rows,
            charts);
    }

    /// <summary>The metrics of C-4. Max drawdown is the non-negative fraction of M8, shown positive as on the result page.</summary>
    private static List<MetricCell> Cells(PerformanceMetrics metrics) =>
    [
        Return(metrics.CumulativeReturn),
        Return(metrics.AnnualizedReturn),
        Percent(metrics.AnnualizedVolatility),
        Ratio(metrics.SharpeRatio),
        Ratio(metrics.SortinoRatio),
        Ratio(metrics.CalmarRatio),
        Percent(metrics.MaxDrawdown),
        new(metrics.TradeCount.ToString(CultureInfo.InvariantCulture), metrics.TradeCount.ToString(CultureInfo.InvariantCulture)),
        Ratio(metrics.ProfitFactor),
        Percent(metrics.Exposure),
    ];

    private static MetricCell Return(double fraction) =>
        new(DisplayFormat.Percent(fraction), ResultTradesViewModel.SortNumber(fraction), ValueTone.Of(fraction));

    private static MetricCell Percent(double fraction) => new(DisplayFormat.Percent(fraction), ResultTradesViewModel.SortNumber(fraction));

    private static MetricCell Ratio(double? value) =>
        new(DisplayFormat.Ratio(value), value is { } defined ? ResultTradesViewModel.SortNumber(defined) : null);

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
}
