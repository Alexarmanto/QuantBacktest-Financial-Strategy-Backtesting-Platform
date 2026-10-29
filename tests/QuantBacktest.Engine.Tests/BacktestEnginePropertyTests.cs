using QuantBacktest.Core.Models;
using QuantBacktest.Engine.Tests.TestSupport;
using QuantBacktest.Fakes;

namespace QuantBacktest.Engine.Tests;

/// <summary>
/// Properties that must hold for any input, checked on 5 synthetic series with random signals (fixed seeds).
/// </summary>
public class BacktestEnginePropertyTests
{
    public static IEnumerable<object[]> Seeds() => new[] { 1, 2, 3, 4, 5 }.Select(seed => new object[] { seed });

    private static BacktestResult RunRandom(int seed, ExecutionSettings settings)
    {
        PriceSeries series = SyntheticPrices.Generate("SYN", Timeframe.Daily, barCount: 300, seed: seed);

        // pseudo-random signals, the same for the same seed
        Random random = new(seed);
        List<Signal> signals = new();
        for (int i = 0; i < series.Bars.Count; i++)
        {
            int draw = random.Next(10);
            signals.Add(draw == 0 ? Signal.Buy : draw == 1 ? Signal.Sell : Signal.Hold);
        }

        BacktestRequest request = ReferenceSeries.Request(settings);
        return new BacktestEngine().Run(request, series, new ScriptedStrategy(signals));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EquityIsNeverNegative_AndHasOnePointPerBar(int seed)
    {
        BacktestResult result = RunRandom(seed, ReferenceSeries.Settings());

        result.Equity.Should().HaveCount(result.Bars.Count);
        result.Equity.Should().OnlyContain(point => point.StrategyEquity >= 0 && point.BenchmarkEquity >= 0);
        result.Equity.Select(point => point.Date).Should().Equal(result.Bars.Select(bar => bar.Date));
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FinalEquity_IsTheCapitalPlusTheProfitOfEveryTrade(int seed)
    {
        BacktestResult result = RunRandom(seed, ReferenceSeries.Settings());

        decimal expected = 10_000m + result.Trades.Sum(trade => trade.NetProfit);

        result.Equity[^1].StrategyEquity.Should().BeApproximately(expected, 0.000000001m);
        result.Trades.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Trades_AreOrderedAndNeverOverlap(int seed)
    {
        BacktestResult result = RunRandom(seed, ReferenceSeries.Settings());

        for (int i = 0; i < result.Trades.Count; i++)
        {
            Trade trade = result.Trades[i];
            trade.EntryDate.Should().BeOnOrBefore(trade.ExitDate);
            trade.HoldingBars.Should().BeGreaterThanOrEqualTo(0);
            if (i > 0)
            {
                trade.EntryDate.Should().BeOnOrAfter(result.Trades[i - 1].ExitDate);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void WithoutFeesAndSlippage_ProfitIsPriceDifferenceTimesQuantity(int seed)
    {
        ExecutionSettings settings = ReferenceSeries.Settings(fee: 0m);

        BacktestResult result = RunRandom(seed, settings);

        foreach (Trade trade in result.Trades)
        {
            decimal expected = (trade.ExitPrice - trade.EntryPrice) * trade.Quantity;
            trade.NetProfit.Should().BeApproximately(expected, 0.000000001m);
            trade.Fees.Should().Be(0m);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Exposure_IsTheShareOfPointsInPosition(int seed)
    {
        BacktestResult result = RunRandom(seed, ReferenceSeries.Settings());

        double expected = result.Equity.Count(point => point.InPosition) / (double)result.Equity.Count;

        result.Metrics.Exposure.Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void NoMetricIsNaNOrInfinite(int seed)
    {
        PerformanceMetrics m = RunRandom(seed, ReferenceSeries.Settings()).Metrics;

        double?[] values =
        {
            m.CumulativeReturn, m.AnnualizedReturn, m.AverageReturnPerPeriod, m.AnnualizedVolatility,
            m.SharpeRatio, m.SortinoRatio, m.CalmarRatio, m.MaxDrawdown, m.WinRate, m.ProfitFactor, m.Exposure,
        };
        foreach (double? value in values)
        {
            if (value != null)
            {
                double.IsFinite(value.Value).Should().BeTrue();
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void WithProtectiveExits_TheInvariantsStillHold(int seed)
    {
        ExecutionSettings settings = ReferenceSeries.Settings(
            slippage: 0.001m, stopLoss: 0.03m, takeProfit: 0.06m, trailingStop: 0.04m);

        BacktestResult result = RunRandom(seed, settings);

        result.Equity.Should().OnlyContain(point => point.StrategyEquity >= 0);
        result.Equity[^1].StrategyEquity.Should().BeApproximately(
            10_000m + result.Trades.Sum(trade => trade.NetProfit), 0.000000001m);
        result.Trades.Should().OnlyContain(trade => trade.HoldingBars >= 0 && trade.EntryDate <= trade.ExitDate);
        result.Trades.Should().Contain(trade => trade.ExitReason != ExitReason.Signal && trade.ExitReason != ExitReason.EndOfData);
        for (int i = 1; i < result.Trades.Count; i++)
        {
            result.Trades[i].EntryDate.Should().BeOnOrAfter(result.Trades[i - 1].ExitDate);
        }
    }
    [Fact]
    public void NoLookAhead_EntryDependsOnTheNextOpen_NotOnTheNextClose() // X3, X4
    {
        // the signal of bar 1 buys at the Open of bar 2
        PriceSeries original = ReferenceSeries.Create();
        ScriptedStrategy strategy = ScriptedStrategy.FromLetters("HBHHHH");
        BacktestRequest request = ReferenceSeries.Request(ReferenceSeries.Settings());

        decimal entryOriginal = new BacktestEngine().Run(request, original, strategy).Trades[0].EntryPrice;
        decimal entryOtherOpen = new BacktestEngine().Run(request, WithBar(original, 2, open: 105m), strategy).Trades[0].EntryPrice;
        decimal entryOtherClose = new BacktestEngine().Run(request, WithBar(original, 2, close: 104.5m), strategy).Trades[0].EntryPrice;

        entryOriginal.Should().Be(104m);
        entryOtherOpen.Should().Be(105m);     // changing the Open of bar i+1 changes the entry price
        entryOtherClose.Should().Be(104m);    // changing the Close of bar i+1 does not
    }

    private static PriceSeries WithBar(PriceSeries series, int index, decimal? open = null, decimal? close = null)
    {
        List<PriceBar> bars = series.Bars.ToList();
        PriceBar old = bars[index];
        bars[index] = old with { Open = open ?? old.Open, Close = close ?? old.Close };
        return series with { Bars = bars };
    }
}
