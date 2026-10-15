using System.Net;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public sealed class MarketDataServiceTests : IDisposable
{
    private const string SeedDailyCsv = "timestamp,open,high,low,close,volume\n"
        + "2026-01-06,10.8,11.2,10.1,11,150\n"
        + "2026-01-05,10,11,9,10.5,100\n";

    private readonly MarketDataHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ---- D2: no refresh ----

    [Fact]
    public async Task GetSeries_CacheNotEmpty_ReturnsTheCacheWithoutAnyCall() // D2
    {
        await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);
        _harness.WriteSeed("MSFT_daily.csv", SeedDailyCsv);

        var series = await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        _harness.Handler.CallCount.Should().Be(1);
        series.Bars.Select(bar => bar.Date).Should().Equal(new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 7));
    }

    [Fact]
    public async Task GetSeries_EmptyCacheWithSeed_ImportsTheSeedWithoutAnyCall() // D2, P1-D9
    {
        _harness.WriteSeed("MSFT_daily.csv", SeedDailyCsv);

        var series = await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        _harness.Handler.CallCount.Should().Be(0);
        series.Should().BeEquivalentTo(new PriceSeries("MSFT", Timeframe.Daily,
        [
            new PriceBar(new DateOnly(2026, 1, 5), 10m, 11m, 9m, 10.5m, 100),
            new PriceBar(new DateOnly(2026, 1, 6), 10.8m, 11.2m, 10.1m, 11m, 150),
        ], false));
        var cached = await _harness.Cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None);
        cached!.Origin.Should().Be(DataOrigin.Seed);
        cached.LastFetchedUtc.Should().Be(_harness.Time.UtcNow);
    }

    [Fact]
    public async Task GetSeries_EmptyCacheWithoutSeed_CallsTheApiOnceAndStoresTheResult() // D2, P1-D9
    {
        _harness.WriteSeed("AAPL_daily.csv", SeedDailyCsv);

        var series = await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        _harness.Handler.CallCount.Should().Be(1);
        series.Bars.Should().HaveCount(2);
        (await _harness.Cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None))!.Origin.Should().Be(DataOrigin.Api);
    }

    [Fact]
    public async Task GetSeries_WeeklySeedInTheAdjustedFormat_IsAdjusted() // D6, P1-D7
    {
        _harness.WriteSeed("SPY_weekly.csv", "timestamp,open,high,low,close,adjusted close,volume,dividend amount\n"
            + "2026-01-09,98,104,96,100,50,123456,0.0000\n");

        var series = await _harness.Service.GetSeriesAsync("SPY", Timeframe.Weekly);

        series.IsAdjusted.Should().BeTrue();
        series.Bars.Single().Should().Be(new PriceBar(new DateOnly(2026, 1, 9), 49m, 52m, 48m, 50m, 123456));
    }

    // ---- D3: refresh ----

    [Fact]
    public async Task GetSeries_ForceRefresh_CallsTheApiAndMergesWithoutDeletingOlderBars() // D3, P1-D9
    {
        _harness.WriteSeed("MSFT_daily.csv", SeedDailyCsv);
        await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        // Seed: 5th and 6th; API: 6th (replaced) and 7th (added).
        var series = await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true);

        _harness.Handler.CallCount.Should().Be(1);
        series.Bars.Select(bar => bar.Date).Should().Equal(
            new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 7));
        series.Bars[1].Close.Should().Be(11.5m);
        (await _harness.Cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None))!.Origin.Should().Be(DataOrigin.Mixed);
    }

    [Fact]
    public async Task GetSeries_ForceRefreshOnAnApiSeries_KeepsTheApiOrigin() // P1-D9
    {
        await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true);

        _harness.Handler.CallCount.Should().Be(2);
        (await _harness.Cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None))!.Origin.Should().Be(DataOrigin.Api);
    }

    [Fact]
    public async Task GetSeries_ForceRefreshFails_ThrowsAndLeavesTheCacheIntact() // D3
    {
        var failing = CountingHttpMessageHandler.Returning("", HttpStatusCode.InternalServerError);
        using var harness = new MarketDataHarness(failing);
        harness.WriteSeed("MSFT_daily.csv", SeedDailyCsv);
        var before = await harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        var act = () => harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.NetworkError);
        var after = await harness.Cache.GetAsync("MSFT", Timeframe.Daily, CancellationToken.None);
        after!.Series.Bars.Should().Equal(before.Bars);
        after.Origin.Should().Be(DataOrigin.Seed);
    }

    // ---- P1-D9: symbols ----

    [Fact]
    public async Task GetSeries_NormalisesTheSymbol() // P1-D9
    {
        var series = await _harness.Service.GetSeriesAsync("  msft ", Timeframe.Daily);

        series.Symbol.Should().Be("MSFT");
        _harness.Handler.RequestedUris.Single().Query.Should().Contain("symbol=MSFT");
    }

    [Theory] // P1-D9: invalid symbol, SymbolNotFound without any call
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MS FT")]
    [InlineData("TOOLONGSYMBOL")]
    [InlineData("../etc")]
    public async Task GetSeries_InvalidSymbol_ThrowsSymbolNotFoundWithoutAnyCall(string symbol)
    {
        var act = () => _harness.Service.GetSeriesAsync(symbol, Timeframe.Daily);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.SymbolNotFound);
        _harness.Handler.CallCount.Should().Be(0);
    }

    // ---- D4, D8, P1-D10: quota ----

    [Fact]
    public async Task GetSeries_QuotaReached_ThrowsRateLimitExceededWithoutAnyCall() // D4
    {
        using var harness = new MarketDataHarness(dailyLimit: 1);
        await harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        var act = () => harness.Service.GetSeriesAsync("AAPL", Timeframe.Daily);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.RateLimitExceeded);
        harness.Handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSeries_QuotaIsCheckedBeforeTheKey() // D4: quota first
    {
        using var harness = new MarketDataHarness(apiKey: "", dailyLimit: 0);

        var act = () => harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.RateLimitExceeded);
    }

    [Fact]
    public async Task GetSeries_WithoutKey_ThrowsMissingApiKeyWithoutCallingOrCounting() // D4, P1-D10
    {
        using var harness = new MarketDataHarness(apiKey: "");

        var act = () => harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.MissingApiKey);
        harness.Handler.CallCount.Should().Be(0);
        (await harness.Service.GetApiQuotaAsync()).UsedToday.Should().Be(0);
    }

    [Fact]
    public async Task GetSeries_FailedRequest_CountsInTheQuota() // D4, P1-D10
    {
        using var harness = new MarketDataHarness(CountingHttpMessageHandler.Returning("""{ "Error Message": "Invalid API call." }"""));

        var act = () => harness.Service.GetSeriesAsync("NOPE", Timeframe.Daily);

        (await act.Should().ThrowAsync<MarketDataException>()).Which.Kind.Should().Be(MarketDataErrorKind.SymbolNotFound);
        (await harness.Service.GetApiQuotaAsync()).UsedToday.Should().Be(1);
    }

    [Fact]
    public async Task Quota_ResetsOnTheNextUtcDay() // D8, P1-D10
    {
        using var harness = new MarketDataHarness(dailyLimit: 1);
        harness.Time.UtcNow = new DateTimeOffset(2026, 10, 15, 23, 59, 0, TimeSpan.Zero);
        await harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);
        var refused = () => harness.Service.GetSeriesAsync("AAPL", Timeframe.Daily);
        await refused.Should().ThrowAsync<MarketDataException>();

        // 00:30 on the 16th at UTC+2 is 22:30 UTC on the 15th: still the same UTC day.
        harness.Time.UtcNow = new DateTimeOffset(2026, 10, 16, 0, 30, 0, TimeSpan.FromHours(2));
        await refused.Should().ThrowAsync<MarketDataException>();

        harness.Time.UtcNow = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        await harness.Service.GetSeriesAsync("AAPL", Timeframe.Daily);

        harness.Handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetApiQuota_ReportsUsageLimitAndNextUtcMidnight() // D8
    {
        using var harness = new MarketDataHarness(dailyLimit: 25);
        harness.Time.UtcNow = new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);
        await harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);
        await harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true);

        var quota = await harness.Service.GetApiQuotaAsync();

        quota.Should().Be(new ApiQuotaStatus(2, 25, new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero)));
        quota.Remaining.Should().Be(23);
    }

    // ---- P1-D11: concurrency ----

    [Fact]
    public async Task TenSimultaneousCalls_SendOneHttpRequest() // P1-D11, D4
    {
        var release = new TaskCompletionSource();
        var handler = new CountingHttpMessageHandler(async (_, _) =>
        {
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MarketDataHarness.DailyCsv) };
        });
        using var harness = new MarketDataHarness(handler);

        var calls = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily)))
            .ToList();
        release.SetResult();
        var results = await Task.WhenAll(calls);

        handler.CallCount.Should().Be(1);
        results.Should().OnlyContain(series => series.Bars.Count == 2);
    }

    [Fact]
    public async Task TenSimultaneousRefreshes_ShareOneHttpRequest() // P1-D11, D4
    {
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var handler = new CountingHttpMessageHandler(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MarketDataHarness.DailyCsv) };
        });
        using var harness = new MarketDataHarness(handler);
        var first = harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true);
        await entered.Task;

        // The request is in flight: the next refreshes join it.
        var others = Enumerable.Range(0, 9).Select(_ => harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true)).ToList();
        release.SetResult();
        await Task.WhenAll([first, .. others]);

        handler.CallCount.Should().Be(1);
        (await harness.Service.GetApiQuotaAsync()).UsedToday.Should().Be(1);
    }

    [Fact]
    public async Task CallerCancellation_DoesNotCancelTheSharedDownload() // P1-D11
    {
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var handler = new CountingHttpMessageHandler(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MarketDataHarness.DailyCsv) };
        });
        using var harness = new MarketDataHarness(handler);
        using var cancellation = new CancellationTokenSource();
        var cancelled = harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, cancellationToken: cancellation.Token);
        await entered.Task;
        var patient = harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true);

        await cancellation.CancelAsync();
        release.SetResult();

        await ((Func<Task>)(() => cancelled)).Should().ThrowAsync<OperationCanceledException>();
        (await patient).Bars.Should().HaveCount(2);
        handler.CallCount.Should().Be(1);
    }

    // ---- P1-D12, invariants, secrets ----

    [Fact]
    public async Task GetCachedSymbols_ReturnsDistinctSortedSymbols() // P1-D12
    {
        _harness.WriteSeed("SPY_daily.csv", SeedDailyCsv);
        _harness.WriteSeed("SPY_weekly.csv", SeedDailyCsv);
        _harness.WriteSeed("AAPL_daily.csv", SeedDailyCsv);
        await _harness.Service.GetSeriesAsync("SPY", Timeframe.Daily);
        await _harness.Service.GetSeriesAsync("SPY", Timeframe.Weekly);
        await _harness.Service.GetSeriesAsync("AAPL", Timeframe.Daily);

        (await _harness.Service.GetCachedSymbolsAsync()).Should().Equal("AAPL", "SPY");
    }

    [Fact]
    public async Task ReturnedSeries_SatisfyThePriceSeriesInvariants() // CONTRACT section 3
    {
        _harness.WriteSeed("MSFT_daily.csv", SeedDailyCsv + "2026-01-05,99,99,99,99,1\n2026-01-02,10,9,11,10,1\n");
        await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);

        var series = await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily, forceRefresh: true);

        series.Bars.Should().BeInAscendingOrder(bar => bar.Date);
        series.Bars.Select(bar => bar.Date).Should().OnlyHaveUniqueItems();
        series.Bars.Should().OnlyContain(bar => bar.Low > 0 && bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close));
    }

    [Fact]
    public async Task Logs_NeverContainTheKey() // D4, CONTRACT section 12
    {
        await _harness.Service.GetSeriesAsync("MSFT", Timeframe.Daily);
        _harness.Options.DailyRequestLimit = 1;
        var act = () => _harness.Service.GetSeriesAsync("AAPL", Timeframe.Daily);
        await act.Should().ThrowAsync<MarketDataException>();

        _harness.Logs.AllText.Should().NotContain(MarketDataHarness.ApiKey);
    }
}
