using QuantBacktest.Core;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes.Tests;

public class SyntheticPricesTests
{
    [Theory]
    [InlineData(Timeframe.Daily)]
    [InlineData(Timeframe.Weekly)]
    public void Generate_IsDeterministicAndValid(Timeframe timeframe)
    {
        var first = SyntheticPrices.Generate("AAPL", timeframe, 400, seed: 1);
        var second = SyntheticPrices.Generate("AAPL", timeframe, 400, seed: 1);

        first.Bars.Should().Equal(second.Bars);
        first.Bars.Select(b => b.Date).Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
        first.Bars.Should().OnlyContain(b =>
            b.Low > 0 && b.Open > 0 && b.Close > 0 &&
            b.High >= b.Open && b.High >= b.Close &&
            b.Low <= b.Open && b.Low <= b.Close);
    }
}

public class FakeServicesTests
{
    private static readonly FakeCustomStrategyStore Store = new();
    private static readonly FakeStrategyCatalog Catalog = new(Store, new FakeCustomStrategyFactory());

    private static BacktestRequest Request(string strategyId, Timeframe timeframe = Timeframe.Daily) => new(
        "AAPL",
        timeframe,
        strategyId,
        new Dictionary<string, double>(),
        From: null,
        To: null,
        ExecutionSettings.Default with { StopLossFraction = 0.05m });

    [Theory]
    [InlineData("fake-alternating")]
    [InlineData("fake-threshold")]
    [InlineData("custom-trend-dip")]
    public async Task Backtest_ReturnsAConsistentResult(string strategyId)
    {
        var service = new FakeBacktestService(new FakeMarketDataService(), Catalog);

        var result = await service.RunAsync(Request(strategyId));

        result.Equity.Should().HaveCount(result.Bars.Count);
        foreach (var indicator in result.Indicators)
        {
            // OnlyContain fails on an empty list, and a strategy may legitimately have no indicator.
            indicator.Values.Should().HaveCount(result.Bars.Count);
        }
        result.Trades.Should().NotBeEmpty();
        result.Metrics.TradeCount.Should().Be(result.Trades.Count);
        result.Metrics.Exposure.Should().BeInRange(0.0, 1.0);
        result.BenchmarkMetrics.Exposure.Should().Be(1.0);
    }

    [Fact]
    public async Task Backtest_UnknownStrategy_Throws()
    {
        var service = new FakeBacktestService(new FakeMarketDataService(), Catalog);

        var act = () => service.RunAsync(Request("does-not-exist"));

        await act.Should().ThrowAsync<BacktestValidationException>();
    }

    [Fact]
    public async Task Backtest_UnsupportedTimeframe_Throws()
    {
        var service = new FakeBacktestService(new FakeMarketDataService(), Catalog);

        var act = () => service.RunAsync(Request("fake-alternating", Timeframe.Weekly));

        await act.Should().ThrowAsync<BacktestValidationException>();
    }

    [Theory]
    [InlineData("UNKNOWN", MarketDataErrorKind.SymbolNotFound)]
    [InlineData("NOKEY", MarketDataErrorKind.MissingApiKey)]
    [InlineData("LIMIT", MarketDataErrorKind.RateLimitExceeded)]
    public async Task MarketData_SpecialSymbols_Throw(string symbol, MarketDataErrorKind expected)
    {
        var act = () => new FakeMarketDataService().GetSeriesAsync(symbol, Timeframe.Daily);

        var exception = await act.Should().ThrowAsync<MarketDataException>();
        exception.Which.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task Catalog_ContainsBuiltInsThenCustomStrategies()
    {
        var all = await Catalog.GetAllAsync();

        all.Select(s => s.Id).Should().Contain(new[] { "fake-alternating", "fake-threshold", FakeCustomStrategyStore.Example.Id });
        all[^1].Category.Should().Be(StrategyCategory.Custom);
        (await Catalog.FindAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task Sweep_ReturnsOneCellPerCombination()
    {
        var service = new FakeParameterSweepService(Catalog);
        var request = new SweepRequest(
            Request("fake-threshold"),
            new SweepAxis("lookback", 5, 25, 5),
            new SweepAxis("threshold", 0.01, 0.03, 0.01),
            SweepMetric.SharpeRatio);

        var result = await service.RunAsync(request);

        result.XValues.Should().Equal(5, 10, 15, 20, 25);
        result.YValues.Should().HaveCount(3);
        result.Cells.Should().HaveCount(15);
        result.Best.Should().NotBeNull();
    }

    [Fact]
    public async Task MonteCarlo_NeedsEnoughTrades()
    {
        var backtest = await new FakeBacktestService(new FakeMarketDataService(), Catalog).RunAsync(Request("fake-alternating"));
        var tooFew = backtest with { Trades = backtest.Trades.Take(Conventions.MinTradesForMonteCarlo - 1).ToList() };

        new FakeMonteCarloService().Run(backtest, new MonteCarloRequest()).FinalReturnHistogram
            .Should().HaveCount(Conventions.HistogramBinCount);
        var act = () => new FakeMonteCarloService().Run(tooFew, new MonteCarloRequest());
        act.Should().Throw<BacktestValidationException>();
    }

    [Fact]
    public async Task TradesCsv_HasTheContractHeader()
    {
        var backtest = await new FakeBacktestService(new FakeMarketDataService(), Catalog).RunAsync(Request("fake-alternating"));

        var csv = new FakeReportGenerator().GenerateTradesCsv(backtest);

        csv.Should().StartWith("entry_date,entry_price,exit_date,exit_price,quantity,fees,net_profit,return_pct,exit_reason,holding_bars\n");
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(backtest.Trades.Count + 1);
    }
}
