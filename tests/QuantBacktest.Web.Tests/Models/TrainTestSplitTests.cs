using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class TrainTestSplitTests
{
    /// <summary>Business days from Monday 2018-01-01 (SyntheticPrices, daily).</summary>
    private static PriceSeries Series(int count) => SyntheticPrices.Generate("AAPL", Timeframe.Daily, count);

    private static TrainTestSplit Split(PriceSeries series, decimal train, DateOnly? from = null, DateOnly? to = null)
    {
        TrainTestSplit.TryCreate(series, from, to, train, out var split, out var error).Should().BeTrue(error);
        return split!;
    }

    private static string Error(PriceSeries series, decimal train, DateOnly? from = null, DateOnly? to = null)
    {
        TrainTestSplit.TryCreate(series, from, to, train, out var split, out var error).Should().BeFalse();
        split.Should().BeNull();
        return error!;
    }

    [Fact] // PART-4 section 7: "découpage T1 sur 100 barres à 70 % → 70 / 30, dates de coupure" (T1, T2)
    public void Split_100BarsAt70_Gives70And30_WithCutDates()
    {
        var series = Series(100);
        var bars = series.Bars;

        var split = Split(series, 70m);

        split.BarCount.Should().Be(100);
        split.TrainCount.Should().Be(70);
        split.TestCount.Should().Be(30);
        split.TrainFrom.Should().Be(bars[0].Date);
        split.TrainTo.Should().Be(bars[69].Date);
        split.TestFrom.Should().Be(bars[70].Date);
        split.TestTo.Should().Be(bars[99].Date);
        (split.TrainFrom, split.TrainTo, split.TestFrom, split.TestTo).Should().Be(
            (new DateOnly(2018, 1, 1), new DateOnly(2018, 4, 6), new DateOnly(2018, 4, 9), new DateOnly(2018, 5, 18)));
    }

    [Fact] // T1 with X1: the N bars are those with from <= date <= to, bounds included; the cut dates are bar dates
    public void Split_UsesThePeriod_BoundsIncluded()
    {
        var series = Series(300);
        var bars = series.Bars;

        var split = Split(series, 50m, bars[100].Date, bars[199].Date);

        split.BarCount.Should().Be(100);
        (split.TrainFrom, split.TrainTo, split.TestFrom, split.TestTo).Should().Be(
            (bars[100].Date, bars[149].Date, bars[150].Date, bars[199].Date));
    }

    [Fact] // T1: bounds between bars select the bars inside; the dates passed on are the bars' own dates
    public void Split_BoundsBetweenBars_GiveBarDates()
    {
        var series = SyntheticPrices.Generate("AAPL", Timeframe.Weekly, 10); // Fridays from 2018-01-05

        var split = Split(series, 50m, new DateOnly(2018, 1, 1), new DateOnly(2018, 2, 4));

        split.BarCount.Should().Be(5);
        (split.TrainFrom, split.TrainTo, split.TestFrom, split.TestTo).Should().Be(
            (new DateOnly(2018, 1, 5), new DateOnly(2018, 1, 12), new DateOnly(2018, 1, 19), new DateOnly(2018, 2, 2)));
    }

    [Theory] // T1: nTrain = floor(N × trainFraction), in decimal: 57% of 100 is 57 (0.57 × 100 is 56.99… in double)
    [InlineData(100, 57, 57)]
    [InlineData(7, 70, 4)]
    [InlineData(10, 72.5, 7)]
    [InlineData(10, 79.9, 7)]
    [InlineData(4, 50, 2)]
    [InlineData(29, 90, 26)]
    public void Split_TrainCountIsFloor_InDecimal(int count, double train, int expected)
    {
        Split(Series(count), (decimal)train).TrainCount.Should().Be(expected);
    }

    [Theory] // T1: "il faut nTrain >= 2 et N - nTrain >= 2" → the message gives N, nTrain and nTest
    [InlineData(3, 70, "The selected period has 3 bars: a training share of 70% gives 2 training bars and 1 test bar, and each part needs at least 2 bars. Choose a longer period or another training share.")]
    [InlineData(4, 90, "The selected period has 4 bars: a training share of 90% gives 3 training bars and 1 test bar, and each part needs at least 2 bars. Choose a longer period or another training share.")]
    [InlineData(3, 50, "The selected period has 3 bars: a training share of 50% gives 1 training bar and 2 test bars, and each part needs at least 2 bars. Choose a longer period or another training share.")]
    public void Split_PartTooShort_IsError(int count, double train, string message)
    {
        Error(Series(count), (decimal)train).Should().Be(message);
    }

    [Fact] // T1: a period of one bar (2018-01-01 only) gives 0 / 1, refused, singular wording
    public void Split_OneBar_IsError()
    {
        Error(Series(10), 50m, to: new DateOnly(2018, 1, 1)).Should().Be(
            "The selected period has 1 bar: a training share of 50% gives 0 training bars and 1 test bar, and each part needs at least 2 bars. Choose a longer period or another training share.");
    }

    [Fact] // T1 boundary: 19 bars at 90% gives 17 / 2, accepted
    public void Split_19BarsAt90_IsAccepted()
    {
        var split = Split(Series(19), 90m);

        (split.TrainCount, split.TestCount).Should().Be((17, 2));
    }

    [Fact] // T1: an empty period (N = 0) names the range of the series
    public void Split_NoBarsInPeriod_NamesTheSeriesRange()
    {
        var series = Series(100);

        Error(series, 70m, new DateOnly(2030, 1, 1)).Should().Be(
            "The selected period contains no bars of AAPL (daily): its data runs from 2018-01-01 to 2018-05-18.");
    }

    [Fact] // T1: a series without bars cannot be split either
    public void Split_EmptySeries_IsError()
    {
        Error(new PriceSeries("AAPL", Timeframe.Weekly, [], true), 70m).Should().Be("No bars are available for AAPL (weekly).");
    }
}
