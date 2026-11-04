using System.Text.Json;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class MonteCarloChartsViewModelTests
{
    private static readonly HistogramBin[] ThreeBins =
    [
        new(-0.1, 0.0, 10),
        new(0.0, 0.1, 25),
        new(0.1, 0.2, 7),
    ];

    private static MonteCarloResult Simulation(
        IReadOnlyList<HistogramBin>? finalReturnBins = null,
        double actualFinalReturn = 0.05,
        double actualMaxDrawdown = 0.15) =>
        new(
            new MonteCarloRequest(1000, 42),
            12,
            new PercentileSummary(-0.08, 0.0, 0.05, 0.1, 0.18),
            new PercentileSummary(0.05, 0.1, 0.15, 0.2, 0.3),
            0.25,
            actualFinalReturn,
            actualMaxDrawdown,
            finalReturnBins ?? ThreeBins,
            [new HistogramBin(0.0, 0.2, 600), new HistogramBin(0.2, 0.4, 400)]);

    [Theory] // MC6 / HistogramBin: [Lower, Upper), the last bin also includes Upper; outside or non-finite → null
    [InlineData(-0.1, 0)]
    [InlineData(-0.05, 0)]
    [InlineData(0.0, 1)]
    [InlineData(0.1, 2)]
    [InlineData(0.2, 2)]
    [InlineData(-0.1000001, null)]
    [InlineData(0.2000001, null)]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, null)]
    public void ActualBinIndex_FollowsTheBinsOfCore(double value, int? expected)
    {
        MonteCarloChartsViewModel.ActualBinIndex(ThreeBins, value).Should().Be(expected);
    }

    [Fact] // MC6: min = max gives one bin [min, max], which holds the actual value
    public void ActualBinIndex_SingleBin_HoldsItsValue()
    {
        MonteCarloChartsViewModel.ActualBinIndex([new HistogramBin(0.05, 0.05, 1000)], 0.05).Should().Be(0);
    }

    [Fact] // P4-X4: no recount, no rebinning: counts and bounds are those of Core, in Core's order
    public void Rows_AreTheBinsOfCore_Formatted()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation());

        charts.FinalReturn.Rows.Should().Equal(
            new HistogramRow("-10.00% to 0.00%", "10", false),
            new HistogramRow("0.00% to 10.00%", "25", true),
            new HistogramRow("10.00% to 20.00%", "7", false));
        charts.MaxDrawdown.Rows.Select(r => r.Count).Should().Equal("600", "400");
    }

    [Fact] // P4-U16, P4-X4: one camelCase JSON payload; counts unchanged, actual bin index, canvas ids
    public void Json_CarriesTheBinsAndTheActualBin()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation());

        using var json = JsonDocument.Parse(charts.Json);
        var finalReturn = json.RootElement.GetProperty("finalReturn");
        finalReturn.GetProperty("canvasId").GetString().Should().Be(MonteCarloChartsViewModel.FinalReturnCanvasId);
        finalReturn.GetProperty("counts").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(10, 25, 7);
        finalReturn.GetProperty("ranges")[1].GetString().Should().Be("0.00% to 10.00%");
        finalReturn.GetProperty("ticks").EnumerateArray().Select(e => e.GetString()).Should().Equal("-10.00%", "0.00%", "10.00%");
        finalReturn.GetProperty("actualIndex").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("maxDrawdown").GetProperty("actualIndex").GetInt32().Should().Be(0);
    }

    [Fact] // P4-X4 readability (1a, 1b, 1c): axis titles and legend labels come with the data; X ticks are lower bounds
    public void Json_CarriesTheAxisTitles_AndTheLegendLabels()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation());

        using var json = JsonDocument.Parse(charts.Json);
        var root = json.RootElement;
        root.GetProperty("yAxisTitle").GetString().Should().Be("Number of simulations");
        root.GetProperty("countLabel").GetString().Should().Be("Simulations");
        root.GetProperty("actualLabel").GetString().Should().Be("Range containing the actual result");
        root.GetProperty("finalReturn").GetProperty("xAxisTitle").GetString().Should().Be("Final return, lower bound of each range");
        root.GetProperty("maxDrawdown").GetProperty("xAxisTitle").GetString()
            .Should().Be("Max drawdown (trade by trade), lower bound of each range");
    }

    [Fact] // 1a: the X axis titles are the column labels of the percentile table
    public void AxisTitles_MatchThePercentileTable()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation());

        charts.FinalReturn.Title.Should().Be(MonteCarloChartsViewModel.FinalReturnTitle).And.Be("Final return");
        charts.MaxDrawdown.Title.Should().Be(MonteCarloChartsViewModel.MaxDrawdownTitle).And.Be("Max drawdown (trade by trade)");
    }

    [Fact] // 1d: reading sentence under the chart, with the actual value already formatted, when it is inside the bins
    public void Reading_ActualInside_NamesTheHighlightedRange()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation());

        charts.FinalReturn.Reading.Should().Be(
            "Each bar counts the simulations whose final return falls in that range. "
            + "Orange: the range containing the actual result (5.00%).");
        charts.MaxDrawdown.Reading.Should().Be(
            "Each bar counts the simulations whose max drawdown (trade by trade) falls in that range. "
            + "Orange: the range containing the actual result (15.00%).");
    }

    [Fact] // decision H6 + 1d: an actual value outside every bin highlights nothing and is given as text only
    public void ActualOutsideTheBins_IsNullInJson_AndSaidInText()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation(actualFinalReturn: 0.5));

        using var json = JsonDocument.Parse(charts.Json);
        json.RootElement.GetProperty("finalReturn").GetProperty("actualIndex").ValueKind.Should().Be(JsonValueKind.Null);
        charts.FinalReturn.Rows.Should().OnlyContain(r => !r.ContainsActual);
        charts.FinalReturn.Summary.Should().EndWith("Actual value (real trade order): 50.00%, outside the simulated range.");
        charts.FinalReturn.Reading.Should().Be(
            "Each bar counts the simulations whose final return falls in that range. "
            + "Actual value (real trade order): 50.00%, outside the simulated range.");
    }

    [Fact] // 2: drawdowns stay positive, like "Max drawdown" in the Performance table of Result (DisplayFormat.Percent)
    public void Drawdowns_ArePositive_LikeTheResultTable()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation());

        charts.MaxDrawdown.Rows.Select(r => r.Range).Should().Equal("0.00% to 20.00%", "20.00% to 40.00%");
        charts.MaxDrawdown.Reading.Should().Contain("(15.00%)").And.NotContain("-");
    }

    [Fact] // MC6 + JSON safety: a non-finite bound prints "n/a", never "NaN%" (System.Text.Json would throw on NaN)
    public void NonFiniteBound_IsNotAvailable()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation([new HistogramBin(double.NaN, 0.1, 5)]));

        charts.FinalReturn.Rows.Should().ContainSingle().Which.Range.Should().Be("n/a to 10.00%");
        charts.Json.Should().NotContain("NaN");
    }

    [Fact] // P4-X4 accessibility (1e): the aria-label is built from values already computed in C#
    public void Summary_DescribesTheHistogram()
    {
        var charts = MonteCarloChartsViewModel.Create(Simulation());

        charts.FinalReturn.Summary.Should().Be(
            "Histogram: number of simulations by final return range, 1,000 simulations in 3 ranges from -10.00% to 20.00%. "
            + "Actual value (real trade order): 5.00%, in the range 0.00% to 10.00%.");
        charts.MaxDrawdown.CanvasId.Should().Be(MonteCarloChartsViewModel.MaxDrawdownCanvasId);
    }
}
