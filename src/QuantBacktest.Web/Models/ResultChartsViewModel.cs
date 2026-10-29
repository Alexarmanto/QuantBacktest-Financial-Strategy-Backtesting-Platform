using System.Globalization;
using System.Text.Json;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>Two series of the same chart, one value per bar: the strategy and the buy &amp; hold benchmark.</summary>
internal sealed record ComparisonSeries(IReadOnlyList<double> Strategy, IReadOnlyList<double> Benchmark);

/// <summary>One indicator line; null during warm-up (and for any non-finite value).</summary>
internal sealed record ChartLine(string Key, string Label, IReadOnlyList<double?> Values);

/// <summary>One buy or sell triangle at the fill price of a trade (slippage included, stops intrabar).</summary>
internal sealed record ChartMarker(string Date, double Price, string Side, string Text);

internal sealed record PriceChartData(
    string CloseLabel,
    string BuyLabel,
    string SellLabel,
    IReadOnlyList<double> Close,
    IReadOnlyList<ChartLine> Indicators,
    IReadOnlyList<ChartMarker> Markers);

internal sealed record OscillatorChartData(IReadOnlyList<ChartLine> Indicators, IReadOnlyList<double> ReferenceLines);

/// <summary>Everything charts.js draws, one entry per bar of BacktestResult.Bars, aligned on Dates.</summary>
internal sealed record ChartsPayload(
    IReadOnlyList<string> Dates,
    string StrategyLabel,
    string BenchmarkLabel,
    ComparisonSeries Equity,
    ComparisonSeries Drawdown,
    PriceChartData Price,
    OscillatorChartData? Oscillator);

/// <summary>
/// Charts of the result page (P4-U5). The server prepares every value: drawdowns negated and in percent, oscillator
/// reference lines deduplicated, markers paired with their date, fill price and text. charts.js only draws them.
/// Json is printed in the page's single &lt;script type="application/json"&gt; (P4-U16).
/// </summary>
internal sealed record ResultChartsViewModel(
    string Json,
    bool HasOscillator,
    bool LogScaleAvailable,
    string EquitySummary,
    string DrawdownSummary,
    string PriceSummary,
    string? OscillatorSummary)
{
    public const string BuySide = "buy";
    public const string SellSide = "sell";

    public const string CloseLabel = "Close";
    public const string BuyLabel = "Buy fill";
    public const string SellLabel = "Sell fill";

    public const string LogScaleLabel = "Log scale";
    public const string LogScaleUnavailable = "Log scale needs every equity value above zero.";

    /// <summary>
    /// camelCase, and the default encoder: it escapes &lt; &gt; &amp; so a custom strategy label containing
    /// "&lt;/script&gt;" cannot close the script element. The writer always uses invariant numbers.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ResultChartsViewModel Create(BacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var strategyLabel = BacktestResultViewModel.StrategyColumn;
        var benchmarkLabel = BacktestResultViewModel.BenchmarkColumn;
        var dates = result.Bars.Select(bar => FormatDate(bar.Date)).ToList();

        var equity = new ComparisonSeries(
            result.Equity.Select(point => (double)point.StrategyEquity).ToList(),
            result.Equity.Select(point => (double)point.BenchmarkEquity).ToList());

        // X15: drawdowns are non-negative fractions below the peak; the chart draws them downwards, in percent.
        var drawdown = new ComparisonSeries(
            result.Equity.Select(point => NegatedPercent(point.StrategyDrawdown)).ToList(),
            result.Equity.Select(point => NegatedPercent(point.BenchmarkDrawdown)).ToList());

        // ReferenceLines of Price indicators are ignored: P4-U5 only draws reference lines on the oscillator chart,
        // and a horizontal guide on the price axis has no meaning for the price indicators of the contract.
        var priceIndicators = result.Indicators.Where(series => series.Pane == IndicatorPane.Price).ToList();
        var oscillators = result.Indicators.Where(series => series.Pane == IndicatorPane.Oscillator).ToList();

        var markers = result.Trades.SelectMany(Markers).ToList();
        var price = new PriceChartData(
            CloseLabel,
            BuyLabel,
            SellLabel,
            result.Bars.Select(bar => (double)bar.Close).ToList(),
            priceIndicators.Select(Line).ToList(),
            markers);

        // MACD line and signal both carry {0}: one guide per distinct level. No symmetric level is added (ROC: {threshold}).
        var referenceLines = oscillators
            .SelectMany(series => series.ReferenceLines)
            .Where(double.IsFinite)
            .Distinct()
            .Order()
            .ToList();
        var oscillator = oscillators.Count == 0
            ? null
            : new OscillatorChartData(oscillators.Select(Line).ToList(), referenceLines);

        var payload = new ChartsPayload(dates, strategyLabel, benchmarkLabel, equity, drawdown, price, oscillator);
        var period = $"{dates[0]} to {dates[^1]}";

        return new ResultChartsViewModel(
            JsonSerializer.Serialize(payload, JsonOptions),
            oscillator is not null,
            result.Equity.All(point => point.StrategyEquity > 0 && point.BenchmarkEquity > 0),
            $"Equity of the {strategyLabel.ToLowerInvariant()} and of {benchmarkLabel.ToLowerInvariant()} from {period}. "
                + $"Final equity: {DisplayFormat.Money(result.Metrics.FinalEquity)} and {DisplayFormat.Money(result.BenchmarkMetrics.FinalEquity)}.",
            $"Drawdowns below the previous peak from {period}. "
                + $"Max drawdown: {DisplayFormat.Percent(result.Metrics.MaxDrawdown)} for the {strategyLabel.ToLowerInvariant()}, "
                + $"{DisplayFormat.Percent(result.BenchmarkMetrics.MaxDrawdown)} for {benchmarkLabel.ToLowerInvariant()}.",
            $"{result.Request.Symbol} closing prices from {period} with {Count(priceIndicators.Count, "indicator")}, "
                + $"{Count(result.Trades.Count, "buy")} and {Count(result.Trades.Count, "sell")} at their fill prices.",
            oscillator is null ? null : DescribeOscillators(oscillators, referenceLines));
    }

    private static IEnumerable<ChartMarker> Markers(Trade trade)
    {
        yield return new ChartMarker(
            FormatDate(trade.EntryDate),
            (double)trade.EntryPrice,
            BuySide,
            $"{BuyLabel} · {DisplayFormat.Money(trade.EntryPrice)}");
        yield return new ChartMarker(
            FormatDate(trade.ExitDate),
            (double)trade.ExitPrice,
            SellSide,
            $"{SellLabel} · {ResultTradesViewModel.Label(trade.ExitReason)} · {DisplayFormat.Money(trade.ExitPrice)}");
    }

    // JSON has no NaN or Infinity (System.Text.Json throws on them): a non-finite value is drawn as a gap.
    private static ChartLine Line(IndicatorSeries series) => new(
        series.Key,
        series.Label,
        series.Values.Select(value => value is { } number && double.IsFinite(number) ? number : (double?)null).ToList());

    private static double NegatedPercent(double fraction)
    {
        var percent = -fraction * 100;

        // A zero drawdown must not print as "-0" in the tooltip.
        return percent == 0 ? 0 : percent;
    }

    private static string DescribeOscillators(IReadOnlyList<IndicatorSeries> oscillators, IReadOnlyList<double> referenceLines)
    {
        var labels = string.Join(", ", oscillators.Select(series => series.Label));
        return referenceLines.Count == 0
            ? $"{labels}, without reference line."
            : $"{labels}, with reference lines at {string.Join(", ", referenceLines.Select(BacktestQuery.FormatNumber))}.";
    }

    private static string Count(int count, string noun) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {noun}{(count == 1 ? string.Empty : "s")}";

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
}
