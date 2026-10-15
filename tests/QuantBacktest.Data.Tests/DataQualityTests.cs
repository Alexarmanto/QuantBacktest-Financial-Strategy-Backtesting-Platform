using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public sealed class DataQualityTests : IDisposable
{
    private static readonly DateTimeOffset Fetched = new(2026, 1, 23, 8, 0, 0, TimeSpan.Zero);

    private readonly MarketDataHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task QualityReport_FindsTheFourAnomaliesAtTheRightDates() // D7, P1-D15
    {
        // Daily bars, January 2026 (Monday 5th to Thursday 22nd):
        //   Mon 05 .. Fri 09 : close 100 five times     -> StalePrice on Fri 09 (5th identical close)
        //   Mon 12           : close 130 (+30 %)        -> PriceJump on Mon 12
        //   Tue 13           : close 131, volume 0      -> ZeroVolume on Tue 13
        //   Wed 21           : close 132, 8 days after  -> Gap on Wed 21
        //   Thu 22           : close 133
        // MissingPeriods: weekdays from Mon 05 to Thu 22 = 5 + 5 + 4 = 14, bars = 9, missing = 5 (14, 15, 16, 19, 20).
        // LargestGapDays: 13 -> 21 = 8 (the weekend 09 -> 12 is 3).
        await Store(Timeframe.Daily,
            Bar(5, 100m), Bar(6, 100m), Bar(7, 100m), Bar(8, 100m), Bar(9, 100m),
            Bar(12, 130m), Bar(13, 131m, volume: 0), Bar(21, 132m), Bar(22, 133m));

        var report = await _harness.Service.GetQualityReportAsync("MSFT", Timeframe.Daily);

        report.Anomalies.Select(anomaly => (anomaly.Date, anomaly.Kind)).Should().Equal(
            (Day(9), DataAnomalyKind.StalePrice),
            (Day(12), DataAnomalyKind.PriceJump),
            (Day(13), DataAnomalyKind.ZeroVolume),
            (Day(21), DataAnomalyKind.Gap));
        report.MissingPeriods.Should().Be(5);
        report.LargestGapDays.Should().Be(8);
        report.Should().BeEquivalentTo(new
        {
            Symbol = "MSFT",
            Timeframe = Timeframe.Daily,
            BarCount = 9,
            FirstDate = Day(5),
            LastDate = Day(22),
            IsAdjusted = false,
            Origin = DataOrigin.Api,
            LastFetchedUtc = Fetched,
        });
        report.Anomalies.Single(anomaly => anomaly.Kind == DataAnomalyKind.PriceJump).Description.Should().Contain("+30.0 %");
        report.Anomalies.Single(anomaly => anomaly.Kind == DataAnomalyKind.Gap).Description.Should().Be("8 calendar days without data.");
    }

    [Fact]
    public async Task QualityReport_Weekly_CountsMissingWeeksFromTheGaps() // D7
    {
        // Gaps: 7 (0 missing), 21 (round(3) - 1 = 2, Gap), 10 (round(1.43) - 1 = 0, not above 10), 11 (round(1.57) - 1 = 1, Gap).
        // MissingPeriods = 3, LargestGapDays = 21.
        await Store(Timeframe.Weekly, Bar(2, 10m), Bar(9, 10.5m), Bar(30, 11m), Bar(40, 11.5m), Bar(51, 12m));

        var report = await _harness.Service.GetQualityReportAsync("MSFT", Timeframe.Weekly);

        report.MissingPeriods.Should().Be(3);
        report.LargestGapDays.Should().Be(21);
        report.Anomalies.Select(anomaly => (anomaly.Date, anomaly.Kind)).Should().Equal(
            (Day(30), DataAnomalyKind.Gap),
            (Day(51), DataAnomalyKind.Gap));
    }

    [Fact]
    public async Task QualityReport_LongStaleSequence_GivesOneAnomaly_AndANewSequenceGivesAnother() // D7
    {
        // 7 identical closes (one anomaly on the 5th), a change, then 5 identical closes again (one more anomaly).
        var bars = Enumerable.Range(0, 7).Select(i => Bar(1 + i, 50m))
            .Concat(Enumerable.Range(0, 5).Select(i => Bar(8 + i, 51m)))
            .ToArray();
        await Store(Timeframe.Weekly, bars);

        var report = await _harness.Service.GetQualityReportAsync("MSFT", Timeframe.Weekly);

        report.Anomalies.Where(anomaly => anomaly.Kind == DataAnomalyKind.StalePrice).Select(anomaly => anomaly.Date)
            .Should().Equal(Day(5), Day(12));
    }

    [Theory] // D7: the threshold is strict, in both directions
    [InlineData(125, false)]
    [InlineData(125.01, true)]
    [InlineData(75, false)]
    [InlineData(74.99, true)]
    public async Task QualityReport_PriceJumpThreshold(double close, bool expected)
    {
        await Store(Timeframe.Daily, Bar(5, 100m), Bar(6, (decimal)close));

        var report = await _harness.Service.GetQualityReportAsync("MSFT", Timeframe.Daily);

        report.Anomalies.Any(anomaly => anomaly.Kind == DataAnomalyKind.PriceJump).Should().Be(expected);
    }

    [Fact]
    public async Task QualityReport_SingleBar_HasNoAnomalyAndNoGap() // D7: a short series never throws
    {
        await Store(Timeframe.Daily, Bar(5, 100m));

        var report = await _harness.Service.GetQualityReportAsync("MSFT", Timeframe.Daily);

        report.Anomalies.Should().BeEmpty();
        report.MissingPeriods.Should().Be(0);
        report.LargestGapDays.Should().Be(0);
    }

    [Fact]
    public async Task QualityReport_NothingCached_ThrowsNotCachedWithoutCallingTheApi() // D7, P1-D15
    {
        _harness.WriteSeed("MSFT_daily.csv", MarketDataHarness.DailyCsv);

        var act = () => _harness.Service.GetQualityReportAsync(" msft ", Timeframe.Daily);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.NotCached);
        _harness.Handler.CallCount.Should().Be(0);
        (await _harness.Cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None)).Should().BeNull("the seed is not imported either");
    }

    [Fact]
    public async Task QualityReport_ReportsOriginAndAdjustment() // D7: Origin Api, Seed or Mixed
    {
        _harness.WriteSeed("SPY_weekly.csv", "timestamp,open,high,low,close,adjusted close,volume,dividend amount\n"
            + "2026-01-09,98,104,96,100,50,123456,0.0000\n");
        await _harness.Service.GetSeriesAsync("SPY", Timeframe.Weekly);

        var report = await _harness.Service.GetQualityReportAsync("spy", Timeframe.Weekly);

        report.Origin.Should().Be(DataOrigin.Seed);
        report.IsAdjusted.Should().BeTrue();
        report.Symbol.Should().Be("SPY");
    }

    private Task Store(Timeframe timeframe, params PriceBar[] bars)
        => _harness.Cache.MergeAsync("MSFT", timeframe, bars, false, DataOrigin.Api, Fetched, CancellationToken.None);

    private static DateOnly Day(int dayOfYear) => new DateOnly(2026, 1, 1).AddDays(dayOfYear - 1);

    private static PriceBar Bar(int dayOfYear, decimal close, long volume = 1_000)
        => new(Day(dayOfYear), close, close, close, close, volume);
}
