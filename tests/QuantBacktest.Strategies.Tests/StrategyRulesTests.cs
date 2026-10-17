using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies.Tests;

// One series built by hand per strategy, with the Buy and the Sell at known bars (PART-2 section 7).
// Small parameters are used so the series stay short: the strategy does not check the ranges (S5), the engine does.
public class StrategyRulesTests
{
    private const Signal H = Signal.Hold;
    private const Signal B = Signal.Buy;
    private const Signal S = Signal.Sell;

    [Fact]
    public void SmaCrossover_BuysOnTheCrossAbove_SellsOnTheCrossBelow() // P2-S1, I1, I9
    {
        // SMA(2): -, 4.5, 3.5, 2.5, 2.5, 3.5, 4.5, 4.5, 3.5, 2.5
        // SMA(3): -, -, 4, 3, 2.67, 3, 4, 4.33, 4, 3
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 5, 4, 3, 2, 3, 4, 5, 4, 3, 2 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "fast", 2 }, { "slow", 3 } };

        IReadOnlyList<Signal> signals = new SmaCrossoverStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, H, H, H, B, H, H, S, H);
    }

    [Fact]
    public void BollingerReversion_BuysBelowTheLowerBand_SellsAboveTheMiddleBand() // P2-S2, I4
    {
        // bar 4: window (10, 10, 7), middle 9, lower 7.59, close 7 goes below -> Buy
        // bar 6: window (7, 8, 11), middle 8.67, close goes from 8 (below 8.33) to 11 -> Sell
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 10, 10, 10, 7, 8, 11, 12 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "period", 3 }, { "stdDevs", 1 }, { "exitLevel", 1 } };

        IReadOnlyList<Signal> signals = new BollingerReversionStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, H, H, B, H, S, H);
    }

    [Fact]
    public void RsiReversion_BuysWhenOversold_SellsWhenOverbought() // P2-S3, I5
    {
        // RSI(2): -, -, 0, 50, 75, 87.5
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 9, 8, 9, 10, 11 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "period", 2 } };

        IReadOnlyList<Signal> signals = new RsiReversionStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, B, H, S, S);
    }

    [Fact]
    public void MomentumRoc_BuysWhenRocCrossesAboveTheThreshold_SellsWhenItCrossesBelow() // P2-S4, I6
    {
        // ROC(1): -, 0.02, 0.078, 0.018, -0.107 and threshold 0.05
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 100, 102, 110, 112, 100 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "lookback", 1 }, { "threshold", 0.05 } };

        IReadOnlyList<Signal> signals = new MomentumRocStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, B, S, H);
    }

    [Fact]
    public void MacdCrossover_BuysAndSellsOnTheSignalLineCrosses() // P2-S5, I7
    {
        // line:   -, -, 0, 0, -0.333, -0.278, 0.491, 0.455, 0.298
        // signal: -, -, -, 0, -0.222, -0.259, 0.241, 0.384, 0.326
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 10, 10, 10, 8, 8, 12, 12, 12 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "fast", 2 }, { "slow", 3 }, { "signal", 2 } };

        IReadOnlyList<Signal> signals = new MacdCrossoverStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, H, H, S, H, B, H, S);
    }

    [Fact]
    public void DonchianBreakout_UsesTheHighsAndLowsOfThePreviousBars() // P2-S6, I8
    {
        // highs = close + 0.5, lows = close - 0.5
        // highest high(2): -, -, 11.5, 12.5, 13.5, 13.5   lowest low(2): -, -, 9.5, 10.5, 11.5, 10.5
        double[] closes = new double[] { 10, 11, 12, 13, 11, 9 };
        double[] highs = new double[] { 10.5, 11.5, 12.5, 13.5, 11.5, 9.5 };
        double[] lows = new double[] { 9.5, 10.5, 11.5, 12.5, 10.5, 8.5 };
        List<PriceBar> bars = TestBars.FromCloses(closes, highs, lows);
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "entryLookback", 2 }, { "exitLookback", 2 } };

        IReadOnlyList<Signal> signals = new DonchianBreakoutStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, B, B, S, S);
    }

    [Fact]
    public void BollingerRsiConfirmation_BuysWhenBothAgree_SellsWhenOneSaysExit() // P2-S7
    {
        // bar 4: close 7 < lower 7.59 and RSI(2) = 0 < 30 -> Buy
        // bar 6: close 11 > middle 8.67 -> Sell
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 10, 10, 10, 7, 8, 11 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "period", 3 }, { "stdDevs", 1 }, { "rsiPeriod", 2 } };

        IReadOnlyList<Signal> signals = new BollingerRsiConfirmationStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, H, H, B, H, S);
    }

    [Fact]
    public void BollingerRsiConfirmation_BuyAndSellOnTheSameBar_GivesSell() // S4
    {
        // With a width >= 0 the lower band is under the middle one, so "close < lower" and
        // "close > middle" can never both be true. Only a negative width (refused by the engine)
        // makes the case possible, so we use it here just to check that Sell wins.
        // bar 5: middle 10.17, "lower" 11.82, close 10.5 -> buy (RSI 30.8 < 35) and sell (close > middle)
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 30, 20, 10, 8, 12, 10.5 });
        Dictionary<string, double> parameters = new Dictionary<string, double>
        {
            { "period", 3 }, { "stdDevs", -1 }, { "rsiPeriod", 2 }, { "oversold", 35 },
        };

        IReadOnlyList<Signal> signals = new BollingerRsiConfirmationStrategy().GenerateSignals(bars, parameters);

        signals[5].Should().Be(S);
    }

    [Fact]
    public void ConnorsRsi2_BuysADipInAnUptrend_SellsOnTheBounce() // P2-S8
    {
        // bar 8: close 50 > SMA(9) 43.3, RSI(2) 25 < 40, close not above SMA(2) 55 -> Buy
        // bar 9: close 70 > SMA(2) 60 -> Sell (bars 1 to 6 also give Sell: the engine ignores a Sell when we own nothing)
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 20, 30, 40, 50, 60, 70, 60, 50, 70 });
        Dictionary<string, double> parameters = new Dictionary<string, double>
        {
            { "trendPeriod", 9 }, { "rsiPeriod", 2 }, { "entryThreshold", 40 }, { "exitPeriod", 2 },
        };

        IReadOnlyList<Signal> signals = new ConnorsRsi2Strategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, S, S, S, S, S, S, H, B, S);
    }

    [Fact]
    public void ConsecutiveDownDays_BuysAfterThreeDownBars_SellsOnTheFirstUpBar() // P2-S9
    {
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 9, 8, 7, 8, 7 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "downDays", 3 } };

        IReadOnlyList<Signal> signals = new ConsecutiveDownDaysStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, H, B, S, H);
    }

    [Fact]
    public void ConsecutiveDownDays_DropSmallerThanMinDrop_DoesNotBuy() // P2-S9
    {
        // the fall over the 3 bars is 7 / 10 - 1 = -30 %, smaller than the 35 % asked
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 9, 8, 7, 8, 7 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "downDays", 3 }, { "minDrop", 0.35 } };

        IReadOnlyList<Signal> signals = new ConsecutiveDownDaysStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(H, H, H, H, S, H);
    }

    [Fact]
    public void TurnOfMonth_October2026_BuysOnThe29th_SellsOnThe3rdBarOfTheMonth() // P2-S10
    {
        // business days from Thursday 1 October to Friday 6 November 2026, default parameters
        List<PriceBar> bars = TestBars.BusinessDays(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 6));

        IReadOnlyList<Signal> signals = new TurnOfMonthStrategy().GenerateSignals(bars, new Dictionary<string, double>());

        for (int i = 0; i < bars.Count; i++)
        {
            DateOnly date = bars[i].Date;
            if (date == new DateOnly(2026, 10, 29))
            {
                // only Friday 30 October is left before the end of the month
                signals[i].Should().Be(B, "29 October 2026 has 1 business day left");
            }
            else if (date == new DateOnly(2026, 10, 5) || date == new DateOnly(2026, 11, 4))
            {
                // 1, 2, 5 October and 2, 3, 4 November
                signals[i].Should().Be(S, $"{date} is the 3rd bar of its month");
            }
            else
            {
                signals[i].Should().Be(H, $"{date} is a normal day");
            }
        }
    }

    [Fact]
    public void TurnOfMonth_BuyAndSellOnTheSameBar_GivesSell() // S4
    {
        // the series starts on 29 October: it is 1 business day before the end AND the 1st bar of the month
        List<PriceBar> bars = TestBars.BusinessDays(new DateOnly(2026, 10, 29), new DateOnly(2026, 11, 2));
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "daysBeforeEnd", 1 }, { "daysAfterStart", 1 } };

        IReadOnlyList<Signal> signals = new TurnOfMonthStrategy().GenerateSignals(bars, parameters);

        signals.Should().Equal(S, H, S);
    }

    [Fact]
    public void BuyAndHold_BuysOnEveryBar() // P2-S11
    {
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 9, 11, 12 });

        IReadOnlyList<Signal> signals = new BuyAndHoldStrategy().GenerateSignals(bars, new Dictionary<string, double>());

        signals.Should().Equal(B, B, B, B);
    }

    [Theory]
    [InlineData("sma-crossover", "fast", 50, "slow", 20)]
    [InlineData("sma-crossover", "fast", 30, "slow", 30)]
    [InlineData("rsi-reversion", "oversold", 70, "overbought", 60)]
    [InlineData("macd-crossover", "fast", 26, "slow", 12)]
    [InlineData("bollinger-rsi-confirmation", "oversold", 50, "overbought", 50)]
    [InlineData("connors-rsi2", "exitPeriod", 200, "trendPeriod", 100)]
    public void WrongRelationBetweenParameters_ThrowsBacktestValidationException(string id, string key1, double value1, string key2, double value2) // S5
    {
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 10, 11, 12 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { key1, value1 }, { key2, value2 } };

        bool thrown = false;
        try
        {
            TestStrategies.Find(id).GenerateSignals(bars, parameters);
        }
        catch (BacktestValidationException)
        {
            thrown = true;
        }

        thrown.Should().BeTrue();
    }
}
