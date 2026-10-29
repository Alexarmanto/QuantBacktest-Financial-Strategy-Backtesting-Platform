using QuantBacktest.Core.Models;
using QuantBacktest.Engine.Tests.TestSupport;

namespace QuantBacktest.Engine.Tests;

/// <summary>
/// Scenarios S1 to S5 and S11 of docs/parts/PART-3.md on the 6-bar reference series.
/// Every expected value is recomputed here from the formulas (X6, X11, X13), never copied from the engine.
/// Capital 10 000, fee 0.1 % (0.001).
/// </summary>
public class BacktestEngineScenarioTests
{
    private const decimal Capital = 10_000m;
    private const decimal Fee = 0.001m;
    private const decimal Tolerance = 0.000001m;

    // X6: q = cash / (fill * (1 + fee))
    private static decimal Quantity(decimal buyFill) => Capital / (buyFill * (1 + Fee));

    // X6: proceeds = q * fill * (1 - fee)
    private static decimal Proceeds(decimal quantity, decimal sellFill) => quantity * sellFill * (1 - Fee);

    [Fact]
    public void S1_BuyThenSell_ExecutesAtTheNextOpen() // X4, X6, X11, X12, X13
    {
        BacktestResult result = ReferenceSeries.Run("BHSHHH");

        decimal q = Quantity(102m);            // entry at the Open of bar 1
        decimal proceeds = Proceeds(q, 106m);  // exit at the Open of bar 3
        decimal fees = (q * 102m * Fee) + (q * 106m * Fee);
        decimal netProfit = proceeds - Capital;

        result.Trades.Should().HaveCount(1);
        Trade trade = result.Trades[0];
        trade.EntryDate.Should().Be(ReferenceSeries.Dates[1]);
        trade.EntryPrice.Should().Be(102m);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[3]);
        trade.ExitPrice.Should().Be(106m);
        trade.ExitReason.Should().Be(ExitReason.Signal);
        trade.HoldingBars.Should().Be(2);
        trade.Quantity.Should().BeApproximately(q, Tolerance);
        trade.Fees.Should().BeApproximately(fees, Tolerance);
        trade.NetProfit.Should().BeApproximately(netProfit, Tolerance);
        trade.ReturnFraction.Should().BeApproximately((double)(netProfit / Capital), 1e-6);

        decimal[] expectedEquity = { Capital, q * 103m, q * 105m, proceeds, proceeds, proceeds };
        AssertEquity(result, expectedEquity);
        result.Metrics.Exposure.Should().BeApproximately(2.0 / 6.0, 1e-9);
    }

    [Fact]
    public void S2_PositionStillOpenOnTheLastBar_IsSoldAtTheClose() // X10, X12
    {
        BacktestResult result = ReferenceSeries.Run("BHHHHH");

        decimal q = Quantity(102m);
        decimal proceeds = Proceeds(q, 110m);

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.EndOfData);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[5]);
        trade.ExitPrice.Should().Be(110m);
        trade.HoldingBars.Should().Be(5); // exit index 5 - entry index 1 + 1
        trade.NetProfit.Should().BeApproximately(proceeds - Capital, Tolerance);

        decimal[] expectedEquity = { Capital, q * 103m, q * 105m, q * 104m, q * 108m, proceeds };
        AssertEquity(result, expectedEquity);
        result.Metrics.Exposure.Should().BeApproximately(5.0 / 6.0, 1e-9);
    }

    [Fact]
    public void S3_IgnoredSignals_AreNotExecuted() // X5, X4
    {
        // Sell while flat (bar 0), Buy while long (bar 2), Sell while flat (bar 4), Buy on the last bar (bar 5)
        BacktestResult result = ReferenceSeries.Run("SBBSSB");

        decimal q = Quantity(104m);            // entry at the Open of bar 2
        decimal proceeds = Proceeds(q, 103m);  // exit at the Open of bar 4

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.EntryDate.Should().Be(ReferenceSeries.Dates[2]);
        trade.EntryPrice.Should().Be(104m);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[4]);
        trade.ExitPrice.Should().Be(103m);
        trade.NetProfit.Should().BeApproximately(proceeds - Capital, Tolerance);

        decimal[] expectedEquity = { Capital, Capital, q * 105m, q * 104m, proceeds, proceeds };
        AssertEquity(result, expectedEquity);
        result.Metrics.Exposure.Should().BeApproximately(2.0 / 6.0, 1e-9);
    }

    [Fact]
    public void S4_FromDate_IgnoresEarlierSignalsButKeepsTheWarmUp() // X1, X2, X4
    {
        // the signals of bars 0 and 1 are before the period and must be ignored
        BacktestResult result = ReferenceSeries.Run("BBHBHH", from: ReferenceSeries.Dates[2]);

        decimal q = Quantity(103m);            // entry at the Open of bar 4
        decimal proceeds = Proceeds(q, 110m);  // end of data at the Close of bar 5

        result.Bars.Should().HaveCount(4);
        result.Equity.Should().HaveCount(4);
        result.Bars[0].Date.Should().Be(ReferenceSeries.Dates[2]);

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.EntryDate.Should().Be(ReferenceSeries.Dates[4]);
        trade.EntryPrice.Should().Be(103m);
        trade.ExitReason.Should().Be(ExitReason.EndOfData);
        trade.HoldingBars.Should().Be(2);
        trade.NetProfit.Should().BeApproximately(proceeds - Capital, Tolerance);

        decimal[] expectedEquity = { Capital, Capital, q * 108m, proceeds };
        AssertEquity(result, expectedEquity);
        result.Metrics.Exposure.Should().BeApproximately(2.0 / 4.0, 1e-9);
    }

    [Fact]
    public void S5_NoSignal_GivesFlatEquityAndUndefinedRatios() // X11, M14
    {
        BacktestResult result = ReferenceSeries.Run("HHHHHH");

        result.Trades.Should().BeEmpty();
        AssertEquity(result, new[] { Capital, Capital, Capital, Capital, Capital, Capital });

        PerformanceMetrics m = result.Metrics;
        m.TradeCount.Should().Be(0);
        m.WinRate.Should().Be(0);
        m.AnnualizedVolatility.Should().Be(0);
        m.MaxDrawdown.Should().Be(0);
        m.Exposure.Should().Be(0);
        m.SharpeRatio.Should().BeNull();
        m.SortinoRatio.Should().BeNull();
        m.CalmarRatio.Should().BeNull();
        m.ProfitFactor.Should().BeNull();
    }

    [Fact]
    public void S11_Slippage_WorsensBothFills() // X6
    {
        BacktestResult result = ReferenceSeries.Run("BHSHHH", ReferenceSeries.Settings(slippage: 0.01m));

        decimal buyFill = 102m * 1.01m;   // 103.02
        decimal sellFill = 106m * 0.99m;  // 104.94
        decimal q = Quantity(buyFill);
        decimal proceeds = Proceeds(q, sellFill);

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.EntryPrice.Should().BeApproximately(buyFill, Tolerance);
        trade.ExitPrice.Should().BeApproximately(sellFill, Tolerance);
        trade.NetProfit.Should().BeApproximately(proceeds - Capital, Tolerance);

        decimal[] expectedEquity = { Capital, q * 103m, q * 105m, proceeds, proceeds, proceeds };
        AssertEquity(result, expectedEquity);
    }

    [Fact]
    public void Benchmark_BuysAtTheFirstOpenAndHolds() // X14, M13
    {
        BacktestResult result = ReferenceSeries.Run("HHHHHH");

        decimal q = Capital / (100m * (1 + Fee)); // the Open of bar 0 is 100
        decimal finalEquity = q * 110m;

        result.Equity[0].BenchmarkEquity.Should().BeApproximately(q * 101m, Tolerance);
        result.BenchmarkMetrics.FinalEquity.Should().BeApproximately(finalEquity, Tolerance);
        result.BenchmarkMetrics.CumulativeReturn.Should().BeApproximately((double)(finalEquity / Capital - 1), 1e-9);
        result.BenchmarkMetrics.TradeCount.Should().Be(1);
        result.BenchmarkMetrics.WinRate.Should().Be(1);
        result.BenchmarkMetrics.ProfitFactor.Should().BeNull();
        result.BenchmarkMetrics.Exposure.Should().Be(1);
    }

    [Fact]
    public void Drawdown_StartsAtTheInitialCapital() // X15
    {
        BacktestResult result = ReferenceSeries.Run("BHSHHH");

        // the equity never goes under the capital, so the drawdown of the strategy is 0 everywhere
        result.Equity.Should().OnlyContain(point => point.StrategyDrawdown == 0);

        // the benchmark: (peak - equity) / peak, with the peak starting at the capital
        decimal peak = Capital;
        foreach (EquityPoint point in result.Equity)
        {
            peak = Math.Max(peak, point.BenchmarkEquity);
            double expected = (double)((peak - point.BenchmarkEquity) / peak);
            point.BenchmarkDrawdown.Should().BeApproximately(expected, 1e-12);
        }
    }

    [Fact]
    public void InPositionFlags_CountTheFinalLiquidationAsHeld() // X11
    {
        BacktestResult result = ReferenceSeries.Run("BHHHHH");

        result.Equity.Select(point => point.InPosition).Should().Equal(false, true, true, true, true, true);
    }

    [Fact]
    public void Indicators_AreCutToThePeriod() // X2
    {
        ScriptedStrategy strategy = ScriptedStrategy.FromLetters("HHHHHH");
        strategy.Indicators = new List<IndicatorSeries>
        {
            new("line", "Line", IndicatorPane.Price, new double?[] { null, 1, 2, 3, 4, 5 }, new List<double>()),
        };
        BacktestRequest request = ReferenceSeries.Request(ReferenceSeries.Settings(), from: ReferenceSeries.Dates[2]);

        BacktestResult result = new BacktestEngine().Run(request, ReferenceSeries.Create(), strategy);

        result.Indicators.Should().ContainSingle();
        result.Indicators[0].Values.Should().Equal(2.0, 3.0, 4.0, 5.0);
    }

    [Fact]
    public void ResultFields_ComeFromTheSeriesAndTheStrategy() // P3-E5
    {
        BacktestResult result = ReferenceSeries.Run("BHSHHH");

        result.StrategyName.Should().Be("Scripted strategy");
        result.PricesAdjusted.Should().BeTrue();
        result.Bars.Should().HaveCount(6);
        result.Equity.Select(point => point.Date).Should().Equal(result.Bars.Select(bar => bar.Date));
    }

    [Fact]
    public void SameInputs_GiveEqualResults() // P3-E6
    {
        BacktestResult first = ReferenceSeries.Run("BHSHBH");
        BacktestResult second = ReferenceSeries.Run("BHSHBH");

        second.Metrics.Should().Be(first.Metrics);
        second.BenchmarkMetrics.Should().Be(first.BenchmarkMetrics);
        second.Trades.Should().Equal(first.Trades);
        second.Equity.Should().Equal(first.Equity);
    }

    private static void AssertEquity(BacktestResult result, decimal[] expected)
    {
        result.Equity.Should().HaveCount(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            result.Equity[i].StrategyEquity.Should().BeApproximately(expected[i], Tolerance, $"equity of bar {i}");
        }
    }
}
