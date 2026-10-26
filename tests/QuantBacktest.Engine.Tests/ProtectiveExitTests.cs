using QuantBacktest.Core.Models;
using QuantBacktest.Engine.Tests.TestSupport;

namespace QuantBacktest.Engine.Tests;

/// <summary>
/// Scenarios S6 to S10 of docs/parts/PART-3.md (rules X8, X9, X12) on the 6-bar reference series, plus gap cases.
/// Capital 10 000, fee 0.1 %. Expected values are recomputed from the formulas.
/// </summary>
public class ProtectiveExitTests
{
    private const decimal Capital = 10_000m;
    private const decimal Fee = 0.001m;
    private const decimal Tolerance = 0.000001m;

    private static decimal Quantity(decimal buyFill) => Capital / (buyFill * (1 + Fee));

    private static decimal Proceeds(decimal quantity, decimal sellFill) => quantity * sellFill * (1 - Fee);

    [Fact]
    public void S6_TrailingStop_GapAtTheOpen() // X8 gap
    {
        // entry 102 (Open b1); the highs are 104, 106, 107 so peak = 107 at b4: level = 107 * 0.97 = 103.79
        // the Open of b4 is 103 <= 103.79: exit at the Open, 103
        BacktestResult result = ReferenceSeries.Run("BHHHHH", ReferenceSeries.Settings(trailingStop: 0.03m));

        decimal q = Quantity(102m);
        decimal proceeds = Proceeds(q, 103m);

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.TrailingStop);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[4]);
        trade.ExitPrice.Should().Be(103m);
        trade.HoldingBars.Should().Be(3);
        trade.NetProfit.Should().BeApproximately(proceeds - Capital, Tolerance);

        AssertEquity(result, Capital, q * 103m, q * 105m, q * 104m, proceeds, proceeds);
        result.Metrics.Exposure.Should().BeApproximately(3.0 / 6.0, 1e-9);
    }

    [Fact]
    public void S7_TrailingStop_DuringTheBar() // X8 intrabar
    {
        // peak = 106 at b3 (highs of b1 and b2: 104, 106): level = 106 * 0.98 = 103.88
        // the Open of b3 is 106 (no gap) but its Low is 103.5 <= 103.88: exit at 103.88
        BacktestResult result = ReferenceSeries.Run("BHHHHH", ReferenceSeries.Settings(trailingStop: 0.02m));

        decimal q = Quantity(102m);
        decimal proceeds = Proceeds(q, 103.88m);

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.TrailingStop);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[3]);
        trade.ExitPrice.Should().BeApproximately(103.88m, Tolerance);
        trade.HoldingBars.Should().Be(2);

        AssertEquity(result, Capital, q * 103m, q * 105m, proceeds, proceeds, proceeds);
        result.Metrics.Exposure.Should().BeApproximately(2.0 / 6.0, 1e-9);
    }

    [Fact]
    public void S8_TakeProfit_DuringTheBar() // X8 target
    {
        // target = 102 * 1.03 = 105.06; the High of b2 is 106 >= 105.06
        BacktestResult result = ReferenceSeries.Run("BHHHHH", ReferenceSeries.Settings(takeProfit: 0.03m));

        decimal q = Quantity(102m);
        decimal proceeds = Proceeds(q, 105.06m);

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.TakeProfit);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[2]);
        trade.ExitPrice.Should().BeApproximately(105.06m, Tolerance);
        trade.HoldingBars.Should().Be(1);

        AssertEquity(result, Capital, q * 103m, proceeds, proceeds, proceeds, proceeds);
        result.Metrics.Exposure.Should().BeApproximately(1.0 / 6.0, 1e-9);
    }

    [Fact]
    public void S9_StopComesBeforeTheTarget_OnTheEntryBar() // X8 prudent order, X12
    {
        // on b1: stop = 102 * 0.995 = 101.49 (Low 101 touches it) and target = 102 * 1.01 = 103.02 (High 104 touches it)
        BacktestResult result = ReferenceSeries.Run(
            "BHHHHH", ReferenceSeries.Settings(stopLoss: 0.005m, takeProfit: 0.01m));

        decimal q = Quantity(102m);
        decimal proceeds = Proceeds(q, 101.49m);

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.StopLoss);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[1]);
        trade.ExitPrice.Should().BeApproximately(101.49m, Tolerance);
        trade.HoldingBars.Should().Be(0);
        trade.NetProfit.Should().BeApproximately(proceeds - Capital, Tolerance);

        AssertEquity(result, Capital, proceeds, proceeds, proceeds, proceeds, proceeds);
        result.Metrics.Exposure.Should().Be(0);
    }

    [Fact]
    public void S10_TrailingHigherThanStopLoss_GivesTrailingStopReason() // X9
    {
        // same as S6: trailing level 103.79 is above the stop-loss level 102 * 0.95 = 96.90
        BacktestResult result = ReferenceSeries.Run(
            "BHHHHH", ReferenceSeries.Settings(stopLoss: 0.05m, trailingStop: 0.03m));

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.TrailingStop);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[4]);
        trade.ExitPrice.Should().Be(103m);
        result.Metrics.Exposure.Should().BeApproximately(3.0 / 6.0, 1e-9);
    }

    [Fact]
    public void StopLossAlone_GivesStopLossReason() // X9
    {
        // 10 % stop never hit on the reference series: the position goes to the end
        BacktestResult result = ReferenceSeries.Run("BHHHHH", ReferenceSeries.Settings(stopLoss: 0.1m));

        result.Trades.Should().ContainSingle().Subject.ExitReason.Should().Be(ExitReason.EndOfData);
    }

    [Fact]
    public void TakeProfit_GapAtTheOpen_ExitsAtTheOpen() // X8 gap
    {
        // bar 2 opens at 106, above the target 102 * 1.03 = 105.06: exit at the Open, not at the target
        PriceSeries series = ReferenceSeries.Create();
        List<PriceBar> bars = series.Bars.ToList();
        bars[2] = bars[2] with { Open = 106m };
        BacktestRequest request = ReferenceSeries.Request(ReferenceSeries.Settings(takeProfit: 0.03m));

        BacktestResult result = new BacktestEngine().Run(
            request, series with { Bars = bars }, ScriptedStrategy.FromLetters("BHHHHH"));

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.TakeProfit);
        trade.ExitPrice.Should().Be(106m);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[2]);
    }

    [Fact]
    public void StopLoss_GapDown_ExitsAtTheOpenAndNotAtTheStop() // X8 gap
    {
        // 3 % stop: level = 102 * 0.97 = 98.94. Bar 2 opens at 98, already under it: exit at the Open, 98
        PriceSeries series = ReferenceSeries.Create();
        List<PriceBar> bars = series.Bars.ToList();
        bars[2] = bars[2] with { Open = 98m, Low = 97m, High = 100m, Close = 99m };
        BacktestRequest request = ReferenceSeries.Request(ReferenceSeries.Settings(stopLoss: 0.03m));

        BacktestResult result = new BacktestEngine().Run(
            request, series with { Bars = bars }, ScriptedStrategy.FromLetters("BHHHHH"));

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.ExitReason.Should().Be(ExitReason.StopLoss);
        trade.ExitPrice.Should().Be(98m);
        trade.HoldingBars.Should().Be(1);
    }

    [Fact]
    public void Slippage_AppliesToProtectiveExits() // X6, X8
    {
        // S8 with 1 % slippage: entry 102 * 1.01, the target is based on that fill, exit fill = target * 0.99
        BacktestResult result = ReferenceSeries.Run(
            "BHHHHH", ReferenceSeries.Settings(slippage: 0.01m, takeProfit: 0.03m));

        decimal entryFill = 102m * 1.01m;      // 103.02
        decimal target = entryFill * 1.03m;    // 106.1106: not reached on b2 (High 106), reached on b3 (High 107)
        decimal exitFill = target * 0.99m;

        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.EntryPrice.Should().BeApproximately(entryFill, Tolerance);
        trade.ExitReason.Should().Be(ExitReason.TakeProfit);
        trade.ExitDate.Should().Be(ReferenceSeries.Dates[3]);
        trade.ExitPrice.Should().BeApproximately(exitFill, Tolerance);
    }

    [Fact]
    public void AfterAStop_TheSignalOfTheSameBar_CanOpenANewPosition() // X9
    {
        // b1: bought at 102 and stopped at 101.49 on the same bar; the Buy of b1's close opens again at the Open of b2
        BacktestResult result = ReferenceSeries.Run("BBHHHH", ReferenceSeries.Settings(stopLoss: 0.005m));

        result.Trades.Should().HaveCount(2);
        result.Trades[0].ExitReason.Should().Be(ExitReason.StopLoss);
        result.Trades[1].EntryDate.Should().Be(ReferenceSeries.Dates[2]);
        result.Trades[1].EntryPrice.Should().Be(104m);
    }

    [Fact]
    public void TheTrailingPeak_IgnoresTheBarsBeforeTheEntry() // X8
    {
        // the bars before the entry must not raise the peak (b3 has a High of 107, above the entry price 103)
        BacktestResult result = ReferenceSeries.Run("HHHBHH", ReferenceSeries.Settings(trailingStop: 0.03m));

        // entry at the Open of b4 = 103; on b4 the peak is the entry price: level 99.91, Low 102 > level
        // on b5 the peak is max(103, 108.5) = 108.5: level 105.245; Open 109 is above, Low 108 is above: no exit
        Trade trade = result.Trades.Should().ContainSingle().Subject;
        trade.EntryPrice.Should().Be(103m);
        trade.ExitReason.Should().Be(ExitReason.EndOfData);
    }

    private static void AssertEquity(BacktestResult result, params decimal[] expected)
    {
        result.Equity.Should().HaveCount(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            result.Equity[i].StrategyEquity.Should().BeApproximately(expected[i], Tolerance, $"equity of bar {i}");
        }
    }
}
