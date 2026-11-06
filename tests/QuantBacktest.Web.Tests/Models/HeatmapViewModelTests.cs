using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class HeatmapViewModelTests
{
    private const string Settings = "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0";

    private static OptimizationQuery Query(string metric = "sharpe", string axes = "x=lookback&xmin=1&xmax=6&xstep=5&y=threshold&ymin=0&ymax=0.025&ystep=0.025")
    {
        var url = $"{Settings}&{axes}&metric={metric}";
        BacktestQuery.TryParseOptimization(new QueryCollection(QueryHelpers.ParseQuery(url)), out var query, out _).Should().BeTrue();
        query!.Resolve(BacktestQueryOptimizationTests.Threshold, out var effective).Should().BeEmpty();
        return effective!;
    }

    /// <summary>A 2 × 2 grid written by hand (lookback 1, 6 × threshold 0, 0.025), row-major as O5 says.</summary>
    private static SweepResult Result(
        OptimizationQuery query,
        IReadOnlyList<SweepCell> cells,
        SweepCell? best = null,
        double? average = null,
        bool isolated = false,
        IReadOnlyList<double>? xValues = null,
        IReadOnlyList<double>? yValues = null) =>
        new(query.ToSweepRequest(), xValues ?? [1, 6], yValues ?? [0, 0.025], cells, best, average, isolated);

    private static List<SweepCell> Grid(double? a = 0.1, double? b = 0.2, double? c = 0.3, double? d = 0.4) =>
    [
        new(1, 0, a, 10, null),
        new(6, 0, b, 10, null),
        new(1, 0.025, c, 10, null),
        new(6, 0.025, d, 10, null),
    ];

    private static HeatmapViewModel Create(SweepResult result, OptimizationQuery query, string pathBase = "", bool linkCells = true) =>
        HeatmapViewModel.Create(result, BacktestQueryOptimizationTests.Threshold, query, pathBase, linkCells);

    private static JsonElement Json(HeatmapViewModel model) => JsonDocument.Parse(model.Json).RootElement;

    private static JsonElement Cell(HeatmapViewModel model, int index) => Json(model).GetProperty("cells")[index];

    // ---- JSON of the heatmap ----

    [Fact] // O5, P4-X2: one cell per combination, row by row (for each Y every X), with the axis labels in order
    public void Json_OneCellPerCombination_RowMajor()
    {
        var query = Query();
        var model = Create(Result(query, Grid()), query);
        var json = Json(model);

        json.GetProperty("cells").GetArrayLength().Should().Be(4);
        json.GetProperty("cells").EnumerateArray()
            .Select(c => (c.GetProperty("xi").GetInt32(), c.GetProperty("yi").GetInt32()))
            .Should().Equal((0, 0), (1, 0), (0, 1), (1, 1));
        json.GetProperty("xLabels").EnumerateArray().Select(e => e.GetString()).Should().Equal("1", "6");
        json.GetProperty("yLabels").EnumerateArray().Select(e => e.GetString()).Should().Equal("0", "0.025");
        (model.Columns, model.Rows).Should().Be((2, 2));
    }

    [Fact] // PART-4 section 7 ("cellule null rendue"), section 3 of the contract: null stays null, a non-finite value too
    public void Json_NullAndNonFinite_SerializedAsNull()
    {
        var query = Query();
        var model = Create(Result(query, Grid(a: null, b: double.NaN, c: double.PositiveInfinity)), query);

        for (var i = 0; i < 3; i++)
        {
            Cell(model, i).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
            Cell(model, i).GetProperty("valueText").GetString().Should().Be("n/a");
        }

        Cell(model, 3).GetProperty("value").GetDouble().Should().Be(0.4);
    }

    [Fact] // P4-U16: ResultChartsViewModel.JsonOptions escapes < > &, so a message can never close the script element
    public void Json_UsesJsonOptions_EscapesHtml()
    {
        var query = Query();
        var cells = Grid();
        cells[0] = new SweepCell(1, 0, null, 0, "</script><b>bad</b>");

        var model = Create(Result(query, cells), query);

        model.Json.Should().NotContain("</script>").And.Contain("\\u003C/script\\u003E");
        Cell(model, 0).GetProperty("note").GetString().Should().Be("</script><b>bad</b>");
    }

    // ---- Best cell, banner, reading ----

    [Fact] // O3: the best cell is SweepResult.Best as returned, never recomputed (here it is not the largest value)
    public void Best_ComesFromTheContract_NotRecomputed()
    {
        var query = Query();
        var cells = Grid(a: 0.5, b: 0.9, c: 0.1, d: 0.2);
        var model = Create(Result(query, cells, best: cells[0]), query);

        Enumerable.Range(0, 4).Select(i => Cell(model, i).GetProperty("best").GetBoolean()).Should().Equal(true, false, false, false);
        model.HasBest.Should().BeTrue();
        model.Reading.Should().StartWith("Best Sharpe ratio: 0.50 at Lookback (bars) = 1 and Threshold (fraction) = 0.");
        model.TableRows.Single(r => r.Best).Note.Should().StartWith(HeatmapViewModel.BestNote);
    }

    [Fact] // O3: no non-null value → no best cell, a clear message
    public void NoBest_ShowsMessage()
    {
        var query = Query();
        var model = Create(Result(query, Grid(null, null, null, null)), query);

        model.HasBest.Should().BeFalse();
        model.Reading.Should().Be($"{HeatmapViewModel.NoBestMessage} 4 cells, 4 without a value.");
        model.ScaleLow.Should().BeNull();
        model.ScaleHigh.Should().BeNull();
    }

    [Theory] // PART-4 section 7 (IsIsolatedPeak → banner), O4: the flag of the contract, as received
    [InlineData(true)]
    [InlineData(false)]
    public void IsolatedPeak_IsTheFlagOfTheContract(bool isolated)
    {
        var query = Query();
        var cells = Grid();

        Create(Result(query, cells, best: cells[3], isolated: isolated), query).IsIsolatedPeak.Should().Be(isolated);
    }

    [Fact] // P4-X2, word for word
    public void Texts_AreThoseOfPartFour()
    {
        HeatmapViewModel.OptimisticNote.Should().Be(
            "The best cell is optimistic by construction: validate it on unseen data (Validate page).");
        HeatmapViewModel.IsolatedPeakBanner.Should().Be(
            "Isolated peak: the best result is much better than its neighbours, a classic sign of overfitting.");
    }

    [Fact] // correction D: BestNeighborhoodAverage is shown in the reading sentence when the contract gives it
    public void Reading_IncludesNeighbourhoodAverage_WhenDefined()
    {
        var query = Query();
        var cells = Grid();

        var model = Create(Result(query, cells, best: cells[3], average: 0.077), query);

        model.Reading.Should().Be(
            "Best Sharpe ratio: 0.40 at Lookback (bars) = 6 and Threshold (fraction) = 0.025; average of its neighbours: 0.08. 4 cells, 0 without a value.");
    }

    [Fact] // correction D: no average (no neighbour with a value) → not mentioned
    public void Reading_OmitsNeighbourhoodAverage_WhenNull()
    {
        var query = Query();
        var cells = Grid();

        Create(Result(query, cells, best: cells[3]), query).Reading.Should().NotContain("neighbours");
    }

    // ---- Cells: invalid, undefined, few trades ----

    [Fact] // O2: an invalid combination is an empty cell with its Error, never a link
    public void InvalidCell_HasItsErrorAndNoUrl()
    {
        var query = Query();
        var cells = Grid();
        cells[1] = new SweepCell(6, 0, null, 0, "The fast period must be smaller than the slow period.");

        var cell = Cell(Create(Result(query, cells), query), 1);

        cell.GetProperty("invalid").GetBoolean().Should().BeTrue();
        cell.GetProperty("dim").GetBoolean().Should().BeFalse();
        cell.GetProperty("note").GetString().Should().Be("The fast period must be smaller than the slow period.");
        cell.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact] // O2: an undefined metric (Value null, Error null) is a valid backtest: grey cell, still a link
    public void UndefinedValue_IsNotInvalid_AndKeepsItsLink()
    {
        var query = Query();
        var cells = Grid();
        cells[2] = new SweepCell(1, 0.025, null, 7, null);

        var cell = Cell(Create(Result(query, cells), query), 2);

        cell.GetProperty("invalid").GetBoolean().Should().BeFalse();
        cell.GetProperty("note").GetString().Should().Be(HeatmapViewModel.UndefinedNote);
        cell.GetProperty("url").GetString().Should().NotBeNull();
    }

    [Theory] // P4-X2: TradeCount < 5 → dimmed, with "fewer than 5 trades"
    [InlineData(4, true)]
    [InlineData(0, true)]
    [InlineData(5, false)]
    public void FewTrades_DimmedWithNote(int trades, bool dimmed)
    {
        var query = Query();
        var cells = Grid();
        cells[0] = new SweepCell(1, 0, 0.3, trades, null);

        var cell = Cell(Create(Result(query, cells), query), 0);

        cell.GetProperty("dim").GetBoolean().Should().Be(dimmed);
        (cell.GetProperty("note").ValueKind == JsonValueKind.String
            && cell.GetProperty("note").GetString()!.Contains(HeatmapViewModel.FewTradesNote, StringComparison.Ordinal))
            .Should().Be(dimmed);
    }

    // ---- Formats ----

    [Theory] // P4-U14: ratios with 2 decimals, returns as percentages, null → n/a
    [InlineData("sharpe", 1.234, "1.23")]
    [InlineData("profit-factor", 2.5, "2.50")]
    [InlineData("cumulative-return", 0.1234, "12.34%")]
    [InlineData("annualized-return", -0.05, "-5.00%")]
    public void Tooltip_FormatsWithDisplayFormat(string metric, double value, string text)
    {
        var query = Query(metric);

        Cell(Create(Result(query, Grid(a: value)), query), 0).GetProperty("valueText").GetString().Should().Be(text);
    }

    [Fact] // correction B: X = 0.07500000000000001 is written 0.075 in the label, the tooltip, the table and the URL
    public void FloatingNoise_IsWrittenAs0075_Everywhere()
    {
        var query = Query(axes: "x=threshold&xmin=0.025&xmax=0.075&xstep=0.05&y=lookback&ymin=1&ymax=1&ystep=1");
        SweepCell[] cells = [new(0.025, 1, 0.1, 10, null), new(0.07500000000000001, 1, 0.2, 10, null)];

        var model = Create(Result(query, cells, xValues: [0.025, 0.07500000000000001], yValues: [1]), query);

        Json(model).GetProperty("xLabels")[1].GetString().Should().Be("0.075");
        Cell(model, 1).GetProperty("xText").GetString().Should().Be("0.075");
        model.TableRows[1].X.Text.Should().Be("0.075");
        Cell(model, 1).GetProperty("url").GetString().Should().Contain("p.threshold=0.075")
            .And.NotContain("0.07500000000000001");
    }

    [Fact] // P4-X2: the link of a cell is the canonical /Backtest/Result URL with its parameters, under the path base
    public void CellUrl_IsCanonicalResultUrl()
    {
        var query = Query();

        var model = Create(Result(query, Grid()), query, pathBase: "/app");

        Cell(model, 3).GetProperty("url").GetString().Should().Be(
            "/app/Backtest/Result?" + Settings + "&p.lookback=6&p.threshold=0.025");
        model.TableRows[3].Url.Should().Be(Cell(model, 3).GetProperty("url").GetString());
    }

    [Fact] // P4-X3 reuse: a heatmap without links (train/test)
    public void LinkCellsFalse_HasNoUrl()
    {
        var query = Query();

        Create(Result(query, Grid()), query, linkCells: false).TableRows.Should().OnlyContain(r => r.Url == null);
    }

    // ---- Colour scale (presentation) ----

    /// <summary>One row of cells with these values (null = no value), on the canonical axes; X = 1, 2, 3, …</summary>
    private static HeatmapViewModel ScaleOf(string metric, IReadOnlyList<double?> values)
    {
        var query = Query(metric);
        var cells = values.Select((v, i) => new SweepCell(i + 1, 0, v, 10, null)).ToList();
        return Create(Result(query, cells, xValues: cells.Select(c => c.X).ToList(), yValues: [0]), query);
    }

    private static double?[] Values(string text) =>
        text.Split(';').Select(v => v == "null" ? (double?)null : double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    /// <summary>24 values from 1.1 to 3 above the centre of the profit factor, 2 below it, and one aberrant cell.</summary>
    private static double?[] WithOutlier() =>
        [0.5, 0.75, .. Enumerable.Range(0, 24).Select(k => (double?)Math.Round(1.1 + (k * 1.9 / 23), 10)), 6068.7];

    /// <summary>(side, t) of every cell, read from the JSON; null for a cell without a value.</summary>
    private static List<(int Side, double T)?> SidesAndRanks(HeatmapViewModel model) =>
        Json(model).GetProperty("cells").EnumerateArray()
            .Select(c => c.GetProperty("side").ValueKind == JsonValueKind.Null
                ? ((int, double)?)null
                : (c.GetProperty("side").GetInt32(), c.GetProperty("t").GetDouble()))
            .ToList();

    [Fact] // colour by rank: on one side, t goes from 0 (closest to the centre) to 1 (farthest), evenly
    public void Ranks_OneSide_FromZeroToOne()
    {
        var model = ScaleOf("sharpe", [0.3, 0.1, 0.4, 0.2, 0.5]);

        SidesAndRanks(model).Should().Equal((1, 0.5), (1, 0.0), (1, 0.75), (1, 0.25), (1, 1.0));
    }

    [Fact] // the two sides are ranked independently: each has its own 0 → 1
    public void Ranks_TwoSides_AreIndependent()
    {
        var model = ScaleOf("sharpe", [-0.1, 2, -3, 1, 4]);

        SidesAndRanks(model).Should().Equal((-1, 0.0), (1, 0.5), (-1, 1.0), (1, 0.0), (1, 1.0));
        (model.ScaleLow, model.ScaleCentre, model.ScaleHigh).Should().Be(("-3.00", "0.00", "4.00"));
    }

    [Theory] // only one side present: the other one is not shown in the legend, nothing is invented
    [InlineData("0.1;0.2;0.3", null, "0.30")]
    [InlineData("-0.1;-0.4", "-0.40", null)]
    public void Ranks_OneSideOnly_LegendShowsThatSide(string values, string? low, string? high)
    {
        var model = ScaleOf("sharpe", Values(values));
        var side = low is null ? 1 : -1;

        (model.ScaleLow, model.ScaleHigh).Should().Be((low, high));
        SidesAndRanks(model).Should().OnlyContain(r => r!.Value.Side == side);
    }

    [Fact] // N = 1 on a side: t = 1 (the strongest colour of that side)
    public void Ranks_SingleValue_HasTOne()
    {
        var model = ScaleOf("sharpe", [null, 0.4, null]);

        SidesAndRanks(model).Should().Equal(null, (1, 1.0), null);
        model.ScaleHigh.Should().Be("0.40");
    }

    [Fact] // ties share the average of their ranks: equal values, equal colours
    public void Ranks_Ties_ShareTheAverageRank()
    {
        // above 0: 0.1 (rank 1), 0.2 and 0.2 (ranks 2 and 3 → 2.5), 0.3 (rank 4); t = (r - 1) / 3
        var model = ScaleOf("sharpe", [0.2, 0.1, 0.3, 0.2]);

        SidesAndRanks(model).Should().Equal((1, 0.5), (1, 0.0), (1, 1.0), (1, 0.5));
    }

    [Fact] // a value equal to the centre is on neither side (side 0), drawn with the centre colour
    public void Ranks_ValueEqualToCentre_IsSideZero()
    {
        var model = ScaleOf("profit-factor", [1, 0.5, 3]);

        SidesAndRanks(model).Should().Equal((0, 0.0), (-1, 1.0), (1, 1.0));
    }

    [Fact] // no value at all: no side, no legend scale
    public void Ranks_NoValue_NoLegend()
    {
        var model = ScaleOf("sharpe", [null, null]);

        SidesAndRanks(model).Should().HaveCount(2).And.OnlyContain(r => r == null);
        (model.ScaleLow, model.ScaleHigh).Should().Be((null, null));
    }

    [Fact] // the real case: 6068.7 among 0.5 to 3 no longer flattens the colours; the ranks stay evenly spread
    public void Ranks_Outlier_DoesNotFlattenTheOthers()
    {
        var model = ScaleOf("profit-factor", WithOutlier());
        var ranks = SidesAndRanks(model);

        // above 1: 24 values from 1.1 to 3, then 6068.7 → N = 25, t = k / 24
        var high = ranks.Skip(2).ToList();
        high.Select(r => r!.Value.T).Should().Equal(Enumerable.Range(0, 25).Select(k => k / 24.0));
        high[^1].Should().Be((1, 1.0));
        high[^2]!.Value.T.Should().Be(23 / 24.0);
        // below 1: 0.5 is the farthest (t = 1), 0.75 the closest (t = 0)
        ranks.Take(2).Should().Equal((-1, 1.0), (-1, 0.0));
        // the exact value is kept for the tooltip, the table and the legend
        Cell(model, 26).GetProperty("valueText").GetString().Should().Be("6,068.70");
        model.TableRows[26].Value.Text.Should().Be("6,068.70");
        model.ScaleHigh.Should().Be("6,068.70");
    }

    [Theory] // legend: each end is the real value farthest from the centre on its side, and the centre value
    [InlineData("profit-factor", "0.9;1.5;6068.7", "0.90", "1.00 (break-even)", "6,068.70")]
    [InlineData("sharpe", "0.8;1.01;0.95", null, "0.00", "1.01")]
    [InlineData("cumulative-return", "-0.25;0.1", "-25.00%", "0.00%", "10.00%")]
    public void Legend_ShowsFarthestRealValues(string metric, string values, string? low, string centre, string? high)
    {
        var model = ScaleOf(metric, Values(values));

        (model.ScaleLow, model.ScaleCentre, model.ScaleHigh).Should().Be((low, centre, high));
        HeatmapViewModel.RankNote.Should().Be(
            "Colour shows rank on each side of the centre; hover or open the table for exact values.");
    }

    [Fact] // JSON has no NaN or Infinity: every t is finite, even for one value or ties only
    public void Json_RanksAreFinite()
    {
        foreach (var values in new double?[][] { WithOutlier(), [0.4], [0.2, 0.2, 0.2], [double.NaN, -1, 1] })
        {
            var json = ScaleOf("sharpe", values).Json;

            json.Should().NotContain("NaN").And.NotContain("Infinity");
            JsonDocument.Parse(json).RootElement.GetProperty("cells").EnumerateArray()
                .Where(c => c.GetProperty("t").ValueKind != JsonValueKind.Null)
                .Should().OnlyContain(c => double.IsFinite(c.GetProperty("t").GetDouble()));
        }
    }

    // ---- Text alternative ----

    [Fact] // text alternative: one sortable row per cell; a null value has no sort key (kept last)
    public void Table_OneRowPerCell_WithNotesAndLinks()
    {
        var query = Query();
        var cells = Grid(a: null);

        var rows = Create(Result(query, cells, best: cells[3]), query).TableRows;

        rows.Should().HaveCount(4);
        rows.Select(r => (r.X.Text, r.Y.Text)).Should().Equal(("1", "0"), ("6", "0"), ("1", "0.025"), ("6", "0.025"));
        rows[0].Value.SortValue.Should().BeNull();
        rows[0].Note.Should().Be(HeatmapViewModel.UndefinedNote);
        rows[3].Value.Text.Should().Be("0.40");
        rows[3].Best.Should().BeTrue();
        rows.Should().OnlyContain(r => r.Url != null);
    }
}
