using System.Globalization;
using System.Text.Json;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class ResultChartsViewModelTests
{
    private static readonly DateOnly Day1 = new(2024, 1, 5);
    private static readonly DateOnly Day2 = new(2024, 1, 12);
    private static readonly DateOnly Day3 = new(2024, 1, 19);

    private static readonly PerformanceMetrics StrategyMetrics =
        new(10371.39m, 0.0371, 0.0185, 0.0002, 0.15, 1.2, 2.3, 3.4, 0.1234, 1, 1, null, 0.6);

    private static readonly PerformanceMetrics BenchmarkMetrics =
        new(9500m, -0.05, -0.0255, -0.0001, 0.2, null, null, null, 0.3, 1, 0, null, 1);

    private static BacktestResult Result(
        IReadOnlyList<Trade>? trades = null,
        IReadOnlyList<IndicatorSeries>? indicators = null,
        IReadOnlyList<EquityPoint>? equity = null) =>
        new(
            new BacktestRequest("AAPL", Timeframe.Weekly, "sma-crossover", new Dictionary<string, double>(), null, null, ExecutionSettings.Default),
            "Moving average crossover",
            true,
            [
                new PriceBar(Day1, 100m, 101m, 99m, 100.5m, 0),
                new PriceBar(Day2, 101m, 103m, 100m, 123.45m, 0),
                new PriceBar(Day3, 122m, 125m, 120m, 121.25m, 0),
            ],
            equity ??
            [
                new EquityPoint(Day1, 10000m, 9990m, 0, 0.001, false),
                new EquityPoint(Day2, 10120.5m, 10210m, 0, 0, true),
                new EquityPoint(Day3, 9980.25m, 10050.75m, 0.23, 0.015592, true),
            ],
            trades ?? [],
            StrategyMetrics,
            BenchmarkMetrics,
            indicators ?? []);

    private static IndicatorSeries Series(string key, IndicatorPane pane, IReadOnlyList<double?> values, params double[] referenceLines) =>
        new(key, key.ToUpperInvariant(), pane, values, referenceLines);

    private static Trade Trade(DateOnly entry, decimal entryPrice, DateOnly exit, decimal exitPrice, ExitReason reason = ExitReason.Signal) =>
        new(entry, entryPrice, exit, exitPrice, 10m, 1m, 5m, 0.05, reason, 1);

    private static JsonElement Json(BacktestResult result)
    {
        using var document = JsonDocument.Parse(ResultChartsViewModel.Create(result).Json);
        return document.RootElement.Clone();
    }

    private static double[] Numbers(JsonElement array) => array.EnumerateArray().Select(e => e.GetDouble()).ToArray();

    private static double?[] NullableNumbers(JsonElement array) =>
        array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Null ? (double?)null : e.GetDouble()).ToArray();

    // ---- Axis and equity (P4-U5 (1)) ----

    [Fact] // P4-U5, CONTRACT BacktestResult.Bars: one ISO date per bar of the tested period, in order
    public void Dates_AreIsoOnePerBar()
    {
        Json(Result()).GetProperty("dates").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("2024-01-05", "2024-01-12", "2024-01-19");
    }

    [Fact] // P4-U5 (1): strategy and benchmark equity of each point, unchanged
    public void Equity_MatchesStrategyAndBenchmarkPoints()
    {
        var equity = Json(Result()).GetProperty("equity");

        Numbers(equity.GetProperty("strategy")).Should().Equal(10000, 10120.5, 9980.25);
        Numbers(equity.GetProperty("benchmark")).Should().Equal(9990, 10210, 10050.75);
    }

    [Fact] // P4-U5 (1): the legend reuses the column headers of the metrics table (P4-U4)
    public void Labels_AreTheMetricsTableHeaders()
    {
        var json = Json(Result());

        json.GetProperty("strategyLabel").GetString().Should().Be(BacktestResultViewModel.StrategyColumn).And.Be("Strategy");
        json.GetProperty("benchmarkLabel").GetString().Should().Be(BacktestResultViewModel.BenchmarkColumn).And.Be("Buy & hold");
    }

    [Fact] // P4-U5 (1): log scale offered when every equity value is above zero
    public void LogScale_AvailableWhenEquityIsPositive()
    {
        ResultChartsViewModel.Create(Result()).LogScaleAvailable.Should().BeTrue();
    }

    [Theory] // P4-U5 (1): a log axis cannot show zero or a negative value, for the strategy or the benchmark
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    public void LogScale_UnavailableWhenEquityIsNotPositive(int strategy, int benchmark)
    {
        var equity = new List<EquityPoint>
        {
            new(Day1, 100m, 100m, 0, 0, false),
            new(Day2, strategy, benchmark, 0, 0, false),
            new(Day3, 100m, 100m, 0, 0, false),
        };

        ResultChartsViewModel.Create(Result(equity: equity)).LogScaleAvailable.Should().BeFalse();
    }

    // ---- Drawdown (P4-U5 (2)) ----

    [Fact] // X15, P4-U5 (2): positive fractions below the peak are drawn downwards, in percent (0.23 → -23)
    public void Drawdown_IsNegatedPercent()
    {
        var drawdown = Json(Result()).GetProperty("drawdown");

        var strategy = Numbers(drawdown.GetProperty("strategy"));
        var benchmark = Numbers(drawdown.GetProperty("benchmark"));

        strategy.Should().HaveCount(3);
        strategy[0].Should().Be(0);
        strategy[1].Should().Be(0);
        strategy[2].Should().BeApproximately(-23, 1e-9);
        benchmark.Should().HaveCount(3);
        benchmark[0].Should().BeApproximately(-0.1, 1e-9);
        benchmark[1].Should().Be(0);
        benchmark[2].Should().BeApproximately(-1.5592, 1e-9);
    }

    [Fact] // X15: a zero drawdown is written 0, never -0 (shown as "-0.00%")
    public void Drawdown_ZeroIsNotNegativeZero()
    {
        var model = ResultChartsViewModel.Create(Result());

        model.Json.Should().NotContain("-0,").And.NotContain("-0]");
        Numbers(Json(Result()).GetProperty("drawdown").GetProperty("strategy"))[0]
            .Should().Match(value => value == 0 && !double.IsNegative(value));
    }

    // ---- Price and indicators (P4-U5 (3)) ----

    [Fact] // P4-U5 (3): closes of Bars
    public void Price_CloseOfEachBar()
    {
        Numbers(Json(Result()).GetProperty("price").GetProperty("close")).Should().Equal(100.5, 123.45, 121.25);
    }

    [Fact] // S6: warm-up values stay null
    public void Indicators_WarmUpNullsStayNull()
    {
        var result = Result(indicators: [Series("sma-2", IndicatorPane.Price, [null, 111.975, 122.35])]);

        var line = Json(result).GetProperty("price").GetProperty("indicators")[0];

        line.GetProperty("key").GetString().Should().Be("sma-2");
        line.GetProperty("label").GetString().Should().Be("SMA-2");
        NullableNumbers(line.GetProperty("values")).Should().Equal(null, 111.975, 122.35);
    }

    [Fact] // P4-U5 (3) and (4): Price indicators go on the price chart, Oscillator indicators below it
    public void Indicators_SplitByPane()
    {
        var result = Result(indicators:
        [
            Series("sma-2", IndicatorPane.Price, [null, 1, 2]),
            Series("rsi-2", IndicatorPane.Oscillator, [null, 40, 60], 30, 70),
            Series("ema-2", IndicatorPane.Price, [null, 3, 4]),
        ]);

        var json = Json(result);

        json.GetProperty("price").GetProperty("indicators").EnumerateArray().Select(e => e.GetProperty("key").GetString())
            .Should().Equal("sma-2", "ema-2");
        json.GetProperty("oscillator").GetProperty("indicators").EnumerateArray().Select(e => e.GetProperty("key").GetString())
            .Should().Equal("rsi-2");
    }

    [Fact] // P4-U5 (4): reference lines are only drawn on the oscillator chart; those of a Price indicator are ignored
    public void Indicators_PriceReferenceLinesAreIgnored()
    {
        var result = Result(indicators:
        [
            Series("sma-2", IndicatorPane.Price, [null, 1, 2], 100),
            Series("rsi-2", IndicatorPane.Oscillator, [null, 40, 60], 30),
        ]);

        var json = Json(result);

        json.GetProperty("price").GetProperty("indicators")[0].TryGetProperty("referenceLines", out _).Should().BeFalse();
        Numbers(json.GetProperty("oscillator").GetProperty("referenceLines")).Should().Equal(30);
    }

    [Fact] // Decision 1 of the plan: a non-finite indicator value is a gap; the JSON never contains NaN or Infinity
    public void Indicators_NonFiniteValuesBecomeNull()
    {
        var result = Result(indicators:
        [
            Series("sma-2", IndicatorPane.Price, [double.NaN, double.PositiveInfinity, 2]),
            Series("roc-2", IndicatorPane.Oscillator, [double.NegativeInfinity, 0.5, double.NaN]),
        ]);

        var model = ResultChartsViewModel.Create(result);
        var json = Json(result);

        model.Json.Should().NotContain("NaN").And.NotContain("Infinity");
        NullableNumbers(json.GetProperty("price").GetProperty("indicators")[0].GetProperty("values")).Should().Equal(null, null, 2);
        NullableNumbers(json.GetProperty("oscillator").GetProperty("indicators")[0].GetProperty("values")).Should().Equal(null, 0.5, null);
    }

    // ---- Markers (P4-U5 (3)) ----

    [Fact] // P4-U5 (3), PART-4 section 10: one buy and one sell marker per trade, at its dates and fill prices
    public void Markers_OneBuyOneSellPerTradeAtFillPrices()
    {
        var result = Result(trades:
        [
            Trade(Day1, 100.65m, Day2, 122.10m),
            Trade(Day2, 123.80m, Day3, 119.5m, ExitReason.EndOfData),
        ]);

        var markers = Json(result).GetProperty("price").GetProperty("markers").EnumerateArray()
            .Select(m => (m.GetProperty("date").GetString(), m.GetProperty("price").GetDouble(), m.GetProperty("side").GetString()))
            .ToList();

        markers.Should().Equal(
            ("2024-01-05", 100.65, "buy"),
            ("2024-01-12", 122.1, "sell"),
            ("2024-01-12", 123.8, "buy"),
            ("2024-01-19", 119.5, "sell"));
    }

    [Theory] // P4-U5 (3): fill prices are off the close line, the tooltip says so, with the exit reason of P4-U6
    [InlineData(ExitReason.Signal, "Sell fill · Signal · $122.10")]
    [InlineData(ExitReason.StopLoss, "Sell fill · Stop-loss · $122.10")]
    [InlineData(ExitReason.TakeProfit, "Sell fill · Take-profit · $122.10")]
    [InlineData(ExitReason.TrailingStop, "Sell fill · Trailing stop · $122.10")]
    [InlineData(ExitReason.EndOfData, "Sell fill · End of data · $122.10")]
    public void Markers_TextSaysFillAndExitReason(ExitReason reason, string sellText)
    {
        var markers = Json(Result(trades: [Trade(Day1, 1234.5m, Day2, 122.1m, reason)])).GetProperty("price").GetProperty("markers");

        markers[0].GetProperty("text").GetString().Should().Be("Buy fill · $1,234.50");
        markers[1].GetProperty("text").GetString().Should().Be(sellText);
    }

    [Fact] // X12, P4-U5 (3): a stop hit on the entry bar gives an entry and an exit on the same date
    public void Markers_SameBarEntryAndExit()
    {
        var markers = Json(Result(trades: [Trade(Day2, 123m, Day2, 118m, ExitReason.StopLoss)])).GetProperty("price").GetProperty("markers");

        markers.GetArrayLength().Should().Be(2);
        markers[0].GetProperty("date").GetString().Should().Be("2024-01-12");
        markers[1].GetProperty("date").GetString().Should().Be("2024-01-12");
        markers[0].GetProperty("side").GetString().Should().Be(ResultChartsViewModel.BuySide);
        markers[1].GetProperty("side").GetString().Should().Be(ResultChartsViewModel.SellSide);
    }

    [Fact] // P4-U5: no trade, no marker (the price chart is still drawn)
    public void Markers_EmptyWhenNoTrades()
    {
        var price = Json(Result()).GetProperty("price");

        price.GetProperty("markers").GetArrayLength().Should().Be(0);
        price.GetProperty("close").GetArrayLength().Should().Be(3);
    }

    // ---- Oscillator (P4-U5 (4)) ----

    [Fact] // P4-U5 (4): no Oscillator indicator, no fourth chart
    public void Oscillator_AbsentWhenNoOscillatorIndicator()
    {
        var result = Result(indicators: [Series("sma-2", IndicatorPane.Price, [null, 1, 2])]);

        var model = ResultChartsViewModel.Create(result);

        model.HasOscillator.Should().BeFalse();
        model.OscillatorSummary.Should().BeNull();
        Json(result).GetProperty("oscillator").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact] // P4-U5 (4): MACD line and signal both carry {0}: a single guide
    public void ReferenceLines_DeduplicatedAcrossSeries()
    {
        var result = Result(indicators:
        [
            Series("macd-line", IndicatorPane.Oscillator, [null, 0.4, 0.5], 0),
            Series("macd-signal", IndicatorPane.Oscillator, [null, null, 0.45], 0),
        ]);

        var model = ResultChartsViewModel.Create(result);

        model.HasOscillator.Should().BeTrue();
        Numbers(Json(result).GetProperty("oscillator").GetProperty("referenceLines")).Should().Equal(0);
    }

    [Fact] // P4-U5 (4): no symmetry is assumed (ROC carries {threshold} alone)
    public void ReferenceLines_NoSymmetryAdded()
    {
        var result = Result(indicators: [Series("roc-12", IndicatorPane.Oscillator, [null, 0.01, 0.03], 0.02)]);

        Numbers(Json(result).GetProperty("oscillator").GetProperty("referenceLines")).Should().Equal(0.02);
    }

    [Fact] // P4-U5 (4): levels of several series merged, deduplicated and sorted
    public void ReferenceLines_MergedAndSorted()
    {
        var result = Result(indicators:
        [
            Series("rsi-14", IndicatorPane.Oscillator, [null, 40, 60], 70, 30),
            Series("rsi-2", IndicatorPane.Oscillator, [null, 20, 80], 30),
        ]);

        Numbers(Json(result).GetProperty("oscillator").GetProperty("referenceLines")).Should().Equal(30, 70);
    }

    [Fact] // P4-U5 (4): an oscillator without reference line still gets its chart
    public void ReferenceLines_EmptyIsAllowed()
    {
        var result = Result(indicators: [Series("rsi-2", IndicatorPane.Oscillator, [null, 40, 60])]);

        var model = ResultChartsViewModel.Create(result);

        model.HasOscillator.Should().BeTrue();
        Json(result).GetProperty("oscillator").GetProperty("referenceLines").GetArrayLength().Should().Be(0);
        model.OscillatorSummary.Should().Be("RSI-2, without reference line.");
    }

    // ---- Serialization (P4-U5, P4-U16) ----

    [Fact] // P4-U5, CLAUDE.md conventions: invariant numbers even when the thread culture is fr-FR
    public void Json_IsInvariantUnderFrFr()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var json = ResultChartsViewModel.Create(Result(trades: [Trade(Day1, 100.65m, Day2, 122.1m)])).Json;

            json.Should().Contain("123.45").And.NotContain("123,45");
            json.Should().Contain("\"2024-01-05\"");
            json.Should().Contain("$122.10");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact] // P4-U16: a label cannot close the <script type="application/json"> element
    public void Json_EscapesScriptClosingTag()
    {
        var result = Result(indicators: [new IndicatorSeries("x", "</script><b>", IndicatorPane.Price, [null, 1, 2], [])]);

        var model = ResultChartsViewModel.Create(result);

        model.Json.Should().NotContain("</script>").And.NotContain("<b>");
        Json(result).GetProperty("price").GetProperty("indicators")[0].GetProperty("label").GetString().Should().Be("</script><b>");
    }

    [Fact] // P4-U5: camelCase property names read by charts.js
    public void Json_HasTheShapeReadByChartsJs()
    {
        var json = Json(Result(indicators: [Series("rsi-2", IndicatorPane.Oscillator, [null, 1, 2], 30)]));

        json.EnumerateObject().Select(p => p.Name).Should().Equal(
            "dates", "strategyLabel", "benchmarkLabel", "equity", "drawdown", "price", "oscillator");
        json.GetProperty("price").EnumerateObject().Select(p => p.Name).Should().Equal(
            "closeLabel", "buyLabel", "sellLabel", "close", "indicators", "markers");
        json.GetProperty("oscillator").EnumerateObject().Select(p => p.Name).Should().Equal("indicators", "referenceLines");
    }

    // ---- Accessibility (P4-U5) ----

    [Fact] // P4-U5: each canvas has a summary built from values already computed (Metrics, counts)
    public void Summaries_DescribeEachChart()
    {
        var result = Result(
            trades: [Trade(Day1, 100m, Day2, 122m)],
            indicators:
            [
                Series("sma-2", IndicatorPane.Price, [null, 1, 2]),
                Series("rsi-14", IndicatorPane.Oscillator, [null, 40, 60], 30, 70),
            ]);

        var model = ResultChartsViewModel.Create(result);

        model.EquitySummary.Should().Be(
            "Equity of the strategy and of buy & hold from 2024-01-05 to 2024-01-19. Final equity: $10,371.39 and $9,500.00.");
        model.DrawdownSummary.Should().Be(
            "Drawdowns below the previous peak from 2024-01-05 to 2024-01-19. Max drawdown: 12.34% for the strategy, 30.00% for buy & hold.");
        model.PriceSummary.Should().Be(
            "AAPL closing prices from 2024-01-05 to 2024-01-19 with 1 indicator, 1 buy and 1 sell at their fill prices.");
        model.OscillatorSummary.Should().Be("RSI-14, with reference lines at 30, 70.");
    }
}
