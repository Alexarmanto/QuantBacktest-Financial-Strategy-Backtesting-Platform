using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Strategies.Tests;

// The 11 built-in strategies, for the tests that run on all of them
public static class TestStrategies
{
    public static IStrategy[] All()
    {
        return new IStrategy[]
        {
            new SmaCrossoverStrategy(),
            new BollingerReversionStrategy(),
            new RsiReversionStrategy(),
            new MomentumRocStrategy(),
            new MacdCrossoverStrategy(),
            new DonchianBreakoutStrategy(),
            new BollingerRsiConfirmationStrategy(),
            new ConnorsRsi2Strategy(),
            new ConsecutiveDownDaysStrategy(),
            new TurnOfMonthStrategy(),
            new BuyAndHoldStrategy(),
        };
    }

    public static IStrategy Find(string id)
    {
        foreach (IStrategy strategy in All())
        {
            if (strategy.Id == id)
            {
                return strategy;
            }
        }
        throw new ArgumentException("Unknown strategy " + id);
    }

    // the ids, to run a [Theory] once per strategy
    public static TheoryData<string> Ids()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (IStrategy strategy in All())
        {
            data.Add(strategy.Id);
        }
        return data;
    }

    // S2: with the default parameters, the first bars must all be Hold (indicator warm-up)
    public static int WarmUpBars(string id)
    {
        switch (id)
        {
            case "sma-crossover": return 50;               // SMA(50) defined at bar 49, a cross needs bar 50
            case "bollinger-reversion": return 20;         // bands defined at bar 19, a cross needs bar 20
            case "rsi-reversion": return 14;               // RSI(14) defined at bar 14
            case "momentum-roc": return 15;                // ROC(14) defined at bar 14, a cross needs bar 15
            case "macd-crossover": return 34;              // line at bar 25, signal at bar 33, a cross needs bar 34
            case "donchian-breakout": return 10;           // LowestLow(10) defined at bar 10
            case "bollinger-rsi-confirmation": return 14;  // RSI(14) defined at bar 14
            case "connors-rsi2": return 4;                 // SMA(5) defined at bar 4
            case "consecutive-down-days": return 1;        // bar 0 has no bar before it
            default: return 0;                             // turn-of-month, buy-and-hold: no warm-up
        }
    }
}
