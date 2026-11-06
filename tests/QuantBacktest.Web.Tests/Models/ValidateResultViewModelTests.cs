using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class ValidateResultViewModelTests
{
    private static readonly PriceSeries Series = SyntheticPrices.Generate("AAPL", Timeframe.Daily, 100);

    private static ValidationQuery Query(string url = BacktestQueryValidationTests.Canonical)
    {
        BacktestQuery.TryParseValidation(new QueryCollection(QueryHelpers.ParseQuery(url)), out var query, out var errors)
            .Should().BeTrue(string.Join(" ", errors));
        query!.Resolve(BacktestQueryOptimizationTests.Threshold, out var effective).Should().BeEmpty();
        return effective!;
    }

    private static TrainTestSplit Split()
    {
        TrainTestSplit.TryCreate(Series, null, null, 70m, out var split, out _).Should().BeTrue();
        return split!;
    }

    /// <summary>A 2 × 2 training grid written by hand (lookback 1, 6 × threshold 0, 0.025), best = the last cell.</summary>
    private static SweepResult Sweep(ValidationQuery query, TrainTestSplit split, int bestTrades = 10, bool withBest = true, double? firstValue = 0.1)
    {
        List<SweepCell> cells =
        [
            new(1, 0, firstValue, 10, null),
            new(6, 0, 0.2, 10, null),
            new(1, 0.025, 0.3, 10, null),
            new(6, 0.025, 0.4, bestTrades, null),
        ];
        return new SweepResult(query.TrainGrid(split).ToSweepRequest(), [1, 6], [0, 0.025], cells, withBest ? cells[3] : null, 0.2, false);
    }

    private static async Task<(BacktestResult Train, BacktestResult Test)> RunAsync(ValidationQuery query, TrainTestSplit split, SweepCell best)
    {
        var service = new FakeBacktestService(new FakeMarketDataService(), new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory()));
        var train = await service.RunAsync(query.TrainBacktest(split, best).ToBacktestRequest());
        var test = await service.RunAsync(query.TestBacktest(split, best).ToBacktestRequest());
        return (train, test);
    }

    private static async Task<ValidateResultViewModel> CreateAsync(
        ValidationQuery? query = null,
        Func<SweepResult, SweepResult>? change = null,
        Func<PerformanceMetrics, PerformanceMetrics>? testMetrics = null,
        int bestTrades = 10,
        string pathBase = "")
    {
        query ??= Query();
        var split = Split();
        var sweep = Sweep(query, split, bestTrades);
        sweep = change is null ? sweep : change(sweep);
        var (train, test) = await RunAsync(query, split, sweep.Cells[3]);
        if (testMetrics is not null)
        {
            test = test with { Metrics = testMetrics(test.Metrics) };
        }

        return ValidateResultViewModel.Create(
            sweep, sweep.Best is null ? null : train, sweep.Best is null ? null : test,
            BacktestQueryOptimizationTests.Threshold, query, split, "http://localhost:5080/Validate/Result?x", pathBase);
    }

    // ---- Train vs test table ----

    [Fact] // P4-X3, T3: the 13 fields of PerformanceMetrics side by side, formatted as on the result page, nothing else
    public async Task MetricRows_TrainVsTest_ThirteenFieldsSideBySide()
    {
        var query = Query();
        var split = Split();
        var sweep = Sweep(query, split);
        var (train, test) = await RunAsync(query, split, sweep.Best!);

        var model = ValidateResultViewModel.Create(sweep, train, test, BacktestQueryOptimizationTests.Threshold, query, split, "p", "");

        var definitions = MetricDefinitions.For(Timeframe.Daily);
        model.Metrics.Should().HaveCount(13);
        model.Metrics.Select(r => r.Label).Should().Equal(definitions.Select(d => d.Label));
        model.Metrics.Select(r => r.Train).Should().Equal(definitions.Select(d => d.Format(train.Metrics)));
        model.Metrics.Select(r => r.Test).Should().Equal(definitions.Select(d => d.Format(test.Metrics)));
        model.Metrics.Single(r => r.Label == "Trades").Test.Should().Be(test.Metrics.TradeCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact] // P4-X3: the line of the optimised metric is marked, and only that one
    public async Task MetricRows_OptimizedMetricIsMarked()
    {
        var model = await CreateAsync(Query(BacktestQueryValidationTests.Canonical.Replace("metric=sharpe", "metric=profit-factor")));

        model.Metrics.Where(r => r.IsOptimized).Select(r => r.Label).Should().Equal("Profit factor");
    }

    [Fact] // docs/CONTRACT.md section 3: an undefined ratio is null, shown "n/a"; returns coloured by sign only (P4-U4)
    public async Task MetricRows_NullIsNa_ReturnsColoured()
    {
        var model = await CreateAsync(testMetrics: m => m with { SharpeRatio = null, CumulativeReturn = -0.1 });

        model.Metrics.Single(r => r.Label == "Sharpe ratio").Test.Should().Be("n/a");
        var cumulative = model.Metrics.Single(r => r.Label == "Cumulative return");
        cumulative.Test.Should().Be("-10.00%");
        cumulative.TestTone.Should().Be(ValueTone.Loss);
        model.Metrics.Single(r => r.Label == "Sharpe ratio").TestTone.Should().Be(ValueTone.Neutral);
    }

    [Fact] // M4: the label of AverageReturnPerPeriod follows the timeframe
    public void MetricDefinitions_WeeklyLabel()
    {
        MetricDefinitions.For(Timeframe.Weekly)[3].Label.Should().Be("Average weekly return");
        MetricDefinitions.For(Timeframe.Daily)[3].Label.Should().Be("Average daily return");
    }

    // ---- Reading sentence ----

    [Fact] // T3 "sans autre calcul": the two formatted values side by side, no other number (no gap, ratio or percentage)
    public void ReadingSentence_OnlyTheTwoFormattedValues()
    {
        var metrics = new PerformanceMetrics(12_345.67m, 0.2345, 0.1, 0.001, 0.2, 1.0644, 1.5, 0.7, 0.3, 10, 0.6, 12.6, 0.7);

        var sentence = ValidateResultViewModel.ReadingSentence(SweepMetric.SharpeRatio, metrics, metrics with { SharpeRatio = 0.889 });

        sentence.Should().Be("Sharpe ratio with the best parameters: 1.06 on the training period, 0.89 on the test period.");
        Regex.IsMatch(sentence.Replace("1.06", "", StringComparison.Ordinal).Replace("0.89", "", StringComparison.Ordinal), "[0-9]")
            .Should().BeFalse();

        var returns = ValidateResultViewModel.ReadingSentence(SweepMetric.CumulativeReturn, metrics, metrics with { CumulativeReturn = -0.05 });
        returns.Should().Be("Cumulative return with the best parameters: 23.45% on the training period, -5.00% on the test period.");
    }

    [Fact] // docs/CONTRACT.md section 3: an undefined test metric is "n/a" in the sentence
    public void ReadingSentence_NullTestMetric_IsNa()
    {
        var metrics = new PerformanceMetrics(10_000m, 0, 0, 0, 0, 0.5, null, null, 0, 0, 0, null, 0);

        ValidateResultViewModel.ReadingSentence(SweepMetric.ProfitFactor, metrics with { ProfitFactor = 2 }, metrics)
            .Should().Be("Profit factor with the best parameters: 2.00 on the training period, n/a on the test period.");
    }

    [Theory] // P4-X3: SweepMetrics.Read picks the field named by each of the 6 metrics, nothing computed
    [InlineData(SweepMetric.SharpeRatio, 1.1)]
    [InlineData(SweepMetric.SortinoRatio, 1.2)]
    [InlineData(SweepMetric.CalmarRatio, 1.3)]
    [InlineData(SweepMetric.CumulativeReturn, 0.4)]
    [InlineData(SweepMetric.AnnualizedReturn, 0.5)]
    [InlineData(SweepMetric.ProfitFactor, 1.6)]
    public void SweepMetricsRead_PicksTheField(SweepMetric metric, double expected)
    {
        var metrics = new PerformanceMetrics(10_000m, 0.4, 0.5, 0.01, 0.2, 1.1, 1.2, 1.3, 0.3, 7, 0.5, 1.6, 0.8);

        SweepMetrics.Read(metric, metrics).Should().Be(expected);
        Enum.GetValues<SweepMetric>().Should().HaveCount(6);
    }

    // ---- Best cell ----

    [Theory] // P4-X2 threshold reused (P4-X3): below 5 trades the best parameters get the note; the choice is unchanged
    [InlineData(4, true)]
    [InlineData(5, false)]
    public async Task Best_FewerThanFiveTrades_HasNote(int trades, bool note)
    {
        var model = await CreateAsync(bestTrades: trades);

        model.Best!.FewTradesNote.Should().Be(note ? HeatmapViewModel.FewTradesNote : null);
        model.Best.Parameters.Should().Equal(new SettingRow("Lookback (bars)", "6"), new SettingRow("Threshold (fraction)", "0.025"));
        model.Best.ValueText.Should().Be("0.40");
        model.Best.TradesText.Should().Be(trades.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact] // P4-X3 "Aucune meilleure cellule → message clair": nothing tested, no table, no curve, no test link
    public async Task NoBest_NothingTested()
    {
        var model = await CreateAsync(change: s => s with { Best = null });

        model.Best.Should().BeNull();
        model.Metrics.Should().BeEmpty();
        model.Reading.Should().BeNull();
        model.TestResultUrl.Should().BeNull();
        model.CapitalNote.Should().BeNull();
        model.Charts.TrainSummary.Should().BeNull();
        JsonDocument.Parse(model.Charts.Json).RootElement.GetProperty("periods").GetArrayLength().Should().Be(0);
        model.FullHeatmapUrl.Should().StartWith("/Optimize/Result?");
        ValidateResultViewModel.NoBestMessage.Should().Be(
            "No combination produced a value on the training period, so there are no best parameters to test.");
    }

    // ---- Split, notes ----

    [Fact] // P4-X3 "dates de coupure", T1 / T2: the counts and the four dates; T2 note; same capital for both periods
    public async Task Split_CutDatesAndNotes()
    {
        var model = await CreateAsync();

        model.Split.Should().Equal(
            new SettingRow("Bars in the period", "100"),
            new SettingRow("Training period", "2018-01-01 to 2018-04-06 (70 bars)"),
            new SettingRow("Test period", "2018-04-09 to 2018-05-18 (30 bars)"));
        model.TrainShare.Should().Be("70% training");
        ValidateResultViewModel.WarmUpNote.Should().Contain("rule T2").And.Contain("warm up");
        model.CapitalNote.Should().Be("Each period starts with the same initial capital, $10,000.00.");
    }

    // ---- Heatmap ----

    [Fact] // P4-X3 "la mini-heatmap du train": HeatmapViewModel of the training grid with linkCells: false, same colours
    public async Task Heatmap_IsTheTrainingGrid_NotLinked_SameRanks()
    {
        var query = Query();
        var model = await CreateAsync(query);
        var split = Split();
        var linked = HeatmapViewModel.Create(Sweep(query, split), BacktestQueryOptimizationTests.Threshold, query.TrainGrid(split), "");

        model.Heatmap.CellsLinked.Should().BeFalse();
        model.Heatmap.TableRows.Should().OnlyContain(r => r.Url == null);
        var cells = JsonDocument.Parse(model.Heatmap.Json).RootElement.GetProperty("cells").EnumerateArray().ToList();
        cells.Should().OnlyContain(c => c.GetProperty("url").ValueKind == JsonValueKind.Null);
        var linkedCells = JsonDocument.Parse(linked.Json).RootElement.GetProperty("cells").EnumerateArray().ToList();
        cells.Select(c => (c.GetProperty("side").GetInt32(), c.GetProperty("t").GetDouble()))
            .Should().Equal(linkedCells.Select(c => (c.GetProperty("side").GetInt32(), c.GetProperty("t").GetDouble())));
        model.Heatmap.Reading.Should().Be(linked.Reading);
    }

    // ---- JSON ----

    [Fact] // P4-U16: one JSON object; its heatmap is exactly the payload of HeatmapViewModel; one curve set per period
    public async Task Json_HeatmapPayloadAndBothPeriods()
    {
        var query = Query();
        var split = Split();
        var sweep = Sweep(query, split);
        var (train, test) = await RunAsync(query, split, sweep.Best!);

        var model = ValidateResultViewModel.Create(sweep, train, test, BacktestQueryOptimizationTests.Threshold, query, split, "p", "");
        var root = JsonNode.Parse(model.Charts.Json)!;

        JsonNode.DeepEquals(root["heatmap"], JsonNode.Parse(model.Heatmap.Json)).Should().BeTrue();
        root["strategyLabel"]!.GetValue<string>().Should().Be("Strategy");
        root["benchmarkLabel"]!.GetValue<string>().Should().Be("Buy & hold");
        var periods = root["periods"]!.AsArray();
        periods.Select(p => p!["canvasId"]!.GetValue<string>()).Should().Equal("chart-equity-train", "chart-equity-test");
        periods[0]!["dates"]!.AsArray().Select(d => d!.GetValue<string>())
            .Should().Equal(train.Equity.Select(e => e.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        periods[0]!["dates"]!.AsArray().Should().HaveCount(70);
        periods[1]!["dates"]!.AsArray().Should().HaveCount(30);
        periods[1]!["strategy"]!.AsArray().Select(v => v!.GetValue<double>()).Should().Equal(test.Equity.Select(e => (double)e.StrategyEquity));
        periods[1]!["benchmark"]!.AsArray().Select(v => v!.GetValue<double>()).Should().Equal(test.Equity.Select(e => (double)e.BenchmarkEquity));
        model.Charts.TrainSummary.Should().StartWith("Equity of the strategy and of buy & hold on the training period, from 2018-01-01 to 2018-04-06.");
    }

    [Theory] // docs/CONTRACT.md section 3, P4-U16: a non-finite or null cell value is null in the JSON, never NaN
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(null)]
    public async Task Json_NonFiniteCell_IsNull(double? value)
    {
        var model = await CreateAsync(change: s => s with { Cells = [s.Cells[0] with { Value = value }, .. s.Cells.Skip(1)] });

        model.Charts.Json.Should().NotContain("NaN").And.NotContain("Infinity");
        JsonNode.Parse(model.Charts.Json)!["heatmap"]!["cells"]![0]!["value"].Should().BeNull();
    }

    // ---- Links ----

    [Fact] // P4-X3: the test backtest, the full training heatmap and the form, all canonical (no redirect)
    public async Task Links_AreCanonical()
    {
        var query = Query();
        var split = Split();
        var model = await CreateAsync(query, pathBase: "/app");

        model.EditSettingsUrl.Should().Be("/app/Validate?" + BacktestQueryValidationTests.Canonical);

        var testQuery = model.TestResultUrl!["/app/Backtest/Result?".Length..];
        model.TestResultUrl.Should().StartWith("/app/Backtest/Result?");
        BacktestQuery.TryParse(new QueryCollection(QueryHelpers.ParseQuery(testQuery)), out var test, out _).Should().BeTrue();
        test!.IsCanonical(new QueryString("?" + testQuery)).Should().BeTrue();
        (test.From, test.To).Should().Be((split.TestFrom, split.TestTo));
        test.Parameters.Should().Equal(new Dictionary<string, decimal> { ["lookback"] = 6m, ["threshold"] = 0.025m });

        model.FullHeatmapUrl.Should().StartWith("/app/Optimize/Result?");
        var heatmapQuery = model.FullHeatmapUrl["/app/Optimize/Result?".Length..];
        BacktestQuery.TryParseOptimization(new QueryCollection(QueryHelpers.ParseQuery(heatmapQuery)), out var grid, out _).Should().BeTrue();
        grid!.Resolve(BacktestQueryOptimizationTests.Threshold, out var resolved).Should().BeEmpty();
        resolved!.IsCanonical(new QueryString("?" + heatmapQuery)).Should().BeTrue();
        (grid.Settings.From, grid.Settings.To).Should().Be((split.TrainFrom, split.TrainTo));
        heatmapQuery.Should().NotContain("train=");
    }
}
