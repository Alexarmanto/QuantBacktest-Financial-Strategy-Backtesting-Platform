using System.Text.Json;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class CompareChartsViewModelTests
{
    private static readonly DateOnly Day1 = new(2024, 1, 5);
    private static readonly DateOnly Day2 = new(2024, 1, 12);
    private static readonly DateOnly Day3 = new(2024, 1, 19);

    private static readonly PerformanceMetrics Metrics = new(10371.39m, 0.0371, 0.0185, 0.0002, 0.15, 1.2, 2.3, 3.4, 0.1234, 1, 1, null, 0.6);

    internal static BacktestResult Result(string name, decimal[] strategy, decimal[] benchmark, DateOnly[]? dates = null)
    {
        dates ??= [Day1, Day2, Day3];
        var equity = dates.Select((date, i) => new EquityPoint(date, strategy[i], benchmark[i], 0, 0, false)).ToList();
        return new BacktestResult(
            new BacktestRequest("AAPL", Timeframe.Weekly, name, new Dictionary<string, double>(), null, null, ExecutionSettings.Default),
            name,
            true,
            dates.Select(date => new PriceBar(date, 100m, 101m, 99m, 100m, 0)).ToList(),
            equity,
            [],
            Metrics,
            Metrics with { FinalEquity = benchmark[^1] },
            []);
    }

    private static JsonElement Json(params BacktestResult[] results)
    {
        using var document = JsonDocument.Parse(CompareChartsViewModel.Create(results).Json);
        return document.RootElement.Clone();
    }

    private static double[] Numbers(JsonElement array) => array.EnumerateArray().Select(e => e.GetDouble()).ToArray();

    [Fact] // P4-X1, C1: one curve per successful strategy, in the order given, with its palette index
    public void Charts_OneSeriesPerSuccessfulStrategy_InOrder()
    {
        var json = Json(
            Result("B", [10000m, 10100m, 10200m], [10000m, 9900m, 9800m]),
            Result("A", [10000m, 9000m, 9500.5m], [10000m, 1m, 2m]));

        var series = json.GetProperty("series").EnumerateArray().ToList();
        series.Select(s => s.GetProperty("label").GetString()).Should().Equal("B", "A");
        series.Select(s => s.GetProperty("color").GetInt32()).Should().Equal(1, 2);
        Numbers(series[1].GetProperty("values")).Should().Equal(10000, 9000, 9500.5);
        json.GetProperty("dates").EnumerateArray().Select(e => e.GetString()).Should().Equal("2024-01-05", "2024-01-12", "2024-01-19");
    }

    [Fact] // C1: a single benchmark, the BenchmarkEquity of the first result (the curves are copied, never computed)
    public void Charts_SingleBenchmark_FromFirstResult()
    {
        var json = Json(
            Result("B", [10000m, 10100m, 10200m], [10000m, 9900m, 9800m]),
            Result("A", [10000m, 9000m, 9500m], [10000m, 1m, 2m]));

        json.GetProperty("benchmarkLabel").GetString().Should().Be(BacktestResultViewModel.BenchmarkColumn);
        Numbers(json.GetProperty("benchmark")).Should().Equal(10000, 9900, 9800);
        json.EnumerateObject().Select(p => p.Name).Should().Equal("dates", "benchmarkLabel", "benchmark", "series");
    }

    [Fact] // decision D1, docs/CONTRACT.md section 3: an equity curve with another length violates the contract
    public void Charts_DifferentEquityLength_Throws()
    {
        var first = Result("A", [10000m, 10100m, 10200m], [10000m, 9900m, 9800m]);
        var shorter = Result("B", [10000m, 10100m], [10000m, 9900m], [Day1, Day2]);

        var act = () => CompareChartsViewModel.Create([first, shorter]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'B' has 2 points instead of 3*");
    }

    [Fact] // decision D1, docs/CONTRACT.md section 3: another date at the same index violates the contract
    public void Charts_DifferentEquityDate_Throws()
    {
        var first = Result("A", [10000m, 10100m, 10200m], [10000m, 9900m, 9800m]);
        var shifted = Result("B", [10000m, 10100m, 10200m], [10000m, 9900m, 9800m], [Day1, Day2, new DateOnly(2024, 1, 26)]);

        var act = () => CompareChartsViewModel.Create([first, shifted]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'B' is dated 2024-01-26 at index 2, not 2024-01-19*");
    }

    [Fact] // P4-U16: default encoder, camelCase; a custom strategy name cannot close the <script> element
    public void Charts_Json_DefaultEncoder_EscapesScriptClosing()
    {
        var model = CompareChartsViewModel.Create([Result("</script><b>", [10000m, 1m, 2m], [10000m, 1m, 2m])]);

        model.Json.Should().NotContain("</script>").And.NotContain("<b>");
        Json(Result("</script><b>", [10000m, 1m, 2m], [10000m, 1m, 2m]))
            .GetProperty("series")[0].GetProperty("label").GetString().Should().Be("</script><b>");
    }

    [Fact] // EquityPoint equities are decimals: every value of the JSON is a finite number, never null
    public void Charts_Values_AreFiniteNumbers()
    {
        var json = Json(Result("A", [10000m, 79228162514264337593543950335m, 0m], [10000m, 1m, 2m]));

        json.GetProperty("series")[0].GetProperty("values").EnumerateArray()
            .Should().OnlyContain(e => e.ValueKind == JsonValueKind.Number);
    }

    [Fact] // P4-X1: no successful result, no chart (the page shows the errors only)
    public void Charts_WithoutResult_Throws()
    {
        var act = () => CompareChartsViewModel.Create([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact] // accessibility: the canvas summary names the period and the final equities
    public void Charts_Summary_NamesPeriodAndFinalEquities()
    {
        var model = CompareChartsViewModel.Create([Result("A", [10000m, 1m, 2m], [10000m, 1m, 9500m])]);

        model.Summary.Should().Be(
            "Equity of 1 strategy and of buy & hold from 2024-01-05 to 2024-01-19. Final equity: A $10,371.39, buy & hold $9,500.00.");
    }
}
