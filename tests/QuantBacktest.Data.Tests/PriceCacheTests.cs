using QuantBacktest.Core.Models;
using QuantBacktest.Data.Storage;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public sealed class PriceCacheTests : IDisposable
{
    private static readonly DateTimeOffset FirstFetch = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondFetch = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    private readonly TemporaryDatabase _temporary = new();

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task GetAsync_NothingCached_ReturnsNull() // D2: empty cache
    {
        var cache = new PriceCache(_temporary.Open());

        (await cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task MergeAsync_IntoAnEmptyCache_StoresTheSeriesAndItsMetadata() // P1-D8, P1-D9
    {
        var cache = new PriceCache(_temporary.Open());

        var merged = await cache.MergeAsync("MSFT", Timeframe.Daily, [Bar(6, 11m), Bar(5, 10m)], false, DataOrigin.Seed, FirstFetch, CancellationToken.None);

        merged.Origin.Should().Be(DataOrigin.Seed);
        merged.LastFetchedUtc.Should().Be(FirstFetch);
        merged.Series.Should().BeEquivalentTo(new PriceSeries("MSFT", Timeframe.Daily, [Bar(5, 10m), Bar(6, 11m)], false));
    }

    [Fact]
    public async Task MergeAsync_InsertsNewDatesReplacesExistingOnesAndNeverDeletes() // D3
    {
        var cache = new PriceCache(_temporary.Open());
        await cache.MergeAsync("MSFT", Timeframe.Daily, [Bar(5, 10m), Bar(6, 11m), Bar(7, 12m)], false, DataOrigin.Api, FirstFetch, CancellationToken.None);

        // The new download no longer contains the 5th, corrects the 7th and adds the 8th.
        var merged = await cache.MergeAsync("MSFT", Timeframe.Daily, [Bar(7, 12.5m), Bar(8, 13m)], false, DataOrigin.Api, SecondFetch, CancellationToken.None);

        merged.Series.Bars.Should().Equal(Bar(5, 10m), Bar(6, 11m), Bar(7, 12.5m), Bar(8, 13m));
        merged.LastFetchedUtc.Should().Be(SecondFetch);
    }

    [Fact]
    public async Task MergeAsync_ValuesSurviveARestartExactly() // P1-D8: decimals are stored without loss
    {
        var bar = new PriceBar(new DateOnly(2026, 1, 5), 123.456789m, 130.000001m, 100.1m, 129.999999m, 9_876_543_210);
        await new PriceCache(_temporary.Open()).MergeAsync("MSFT", Timeframe.Weekly, [bar], true, DataOrigin.Api, FirstFetch, CancellationToken.None);

        var reread = await new PriceCache(_temporary.Open()).GetAsync("MSFT", Timeframe.Weekly, CancellationToken.None);

        reread!.Series.Bars.Should().Equal(bar);
        reread.Series.IsAdjusted.Should().BeTrue();
        reread.LastFetchedUtc.Should().Be(FirstFetch);
    }

    [Theory] // P1-D9: Seed + Api = Mixed; an origin stays the same when it is repeated; Mixed stays Mixed
    [InlineData(DataOrigin.Seed, DataOrigin.Api, DataOrigin.Mixed)]
    [InlineData(DataOrigin.Api, DataOrigin.Seed, DataOrigin.Mixed)]
    [InlineData(DataOrigin.Api, DataOrigin.Api, DataOrigin.Api)]
    [InlineData(DataOrigin.Seed, DataOrigin.Seed, DataOrigin.Seed)]
    [InlineData(DataOrigin.Mixed, DataOrigin.Api, DataOrigin.Mixed)]
    public async Task MergeAsync_UpdatesTheOrigin(DataOrigin first, DataOrigin second, DataOrigin expected)
    {
        var cache = new PriceCache(_temporary.Open());
        await cache.MergeAsync("MSFT", Timeframe.Daily, [Bar(5, 10m)], false, first, FirstFetch, CancellationToken.None);

        var merged = await cache.MergeAsync("MSFT", Timeframe.Daily, [Bar(6, 11m)], false, second, SecondFetch, CancellationToken.None);

        merged.Origin.Should().Be(expected);
    }

    [Fact]
    public async Task MergeAsync_AdjustedAndRawBarsMixed_GivesAnUnadjustedSeries() // D6, P1-D7
    {
        var cache = new PriceCache(_temporary.Open());
        var adjusted = await cache.MergeAsync("SPY", Timeframe.Weekly, [Bar(5, 10m)], true, DataOrigin.Api, FirstFetch, CancellationToken.None);
        adjusted.Series.IsAdjusted.Should().BeTrue();

        var mixed = await cache.MergeAsync("SPY", Timeframe.Weekly, [Bar(12, 11m)], false, DataOrigin.Seed, SecondFetch, CancellationToken.None);

        mixed.Series.IsAdjusted.Should().BeFalse();
    }

    [Fact]
    public async Task MergeAsync_ReplacingTheOnlyRawBar_MakesTheSeriesAdjusted() // D6: every bar comes from an adjusted source
    {
        var cache = new PriceCache(_temporary.Open());
        await cache.MergeAsync("SPY", Timeframe.Weekly, [Bar(5, 10m)], false, DataOrigin.Api, FirstFetch, CancellationToken.None);

        var merged = await cache.MergeAsync("SPY", Timeframe.Weekly, [Bar(5, 9m)], true, DataOrigin.Api, SecondFetch, CancellationToken.None);

        merged.Series.IsAdjusted.Should().BeTrue();
    }

    [Fact]
    public async Task Series_AreIsolatedBySymbolAndTimeframe() // P1-D8: key (Symbol, Timeframe, Date)
    {
        var cache = new PriceCache(_temporary.Open());
        await cache.MergeAsync("MSFT", Timeframe.Daily, [Bar(5, 10m)], false, DataOrigin.Api, FirstFetch, CancellationToken.None);
        await cache.MergeAsync("MSFT", Timeframe.Weekly, [Bar(5, 20m)], true, DataOrigin.Api, FirstFetch, CancellationToken.None);
        await cache.MergeAsync("AAPL", Timeframe.Daily, [Bar(5, 30m)], false, DataOrigin.Seed, FirstFetch, CancellationToken.None);

        (await cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None))!.Series.Bars.Should().Equal(Bar(5, 10m));
        (await cache.GetAsync("MSFT", Timeframe.Weekly, CancellationToken.None))!.Series.Bars.Should().Equal(Bar(5, 20m));
        (await cache.GetAsync("AAPL", Timeframe.Daily, CancellationToken.None))!.Origin.Should().Be(DataOrigin.Seed);
        (await cache.GetAsync("AAPL", Timeframe.Weekly, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetSymbolsAsync_ReturnsDistinctSymbolsSorted() // P1-D12
    {
        var cache = new PriceCache(_temporary.Open());
        foreach (var (symbol, timeframe) in new[] { ("SPY", Timeframe.Daily), ("AAPL", Timeframe.Weekly), ("SPY", Timeframe.Weekly), ("JPM", Timeframe.Daily) })
        {
            await cache.MergeAsync(symbol, timeframe, [Bar(5, 10m)], false, DataOrigin.Api, FirstFetch, CancellationToken.None);
        }

        (await cache.GetSymbolsAsync(CancellationToken.None)).Should().Equal("AAPL", "JPM", "SPY");
    }

    [Fact]
    public async Task MergeAsync_WithoutBars_Throws()
    {
        var cache = new PriceCache(_temporary.Open());

        var act = () => cache.MergeAsync("MSFT", Timeframe.Daily, [], false, DataOrigin.Api, FirstFetch, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static PriceBar Bar(int day, decimal close)
        => new(new DateOnly(2026, 1, day), close, close + 1m, close - 1m, close, 1_000 + day);
}
