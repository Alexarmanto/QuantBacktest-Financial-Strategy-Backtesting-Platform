using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S9: buy after several down days in a row, sell on the first up day
internal class ConsecutiveDownDaysStrategy : StrategyBase
{
    // Constructor
    public ConsecutiveDownDaysStrategy()
        : base(
            "consecutive-down-days",
            "Consecutive down days",
            "Buys after several down days in a row and sells on the first day the price goes up again.",
            StrategyCategory.MeanReversion,
            new StrategyDocumentation(
                "After several falls in a row, sellers are often tired and the price bounces back. The strategy buys this bounce and leaves as soon as it happens.",
                "Buy when the close went down downDays times in a row AND the total fall over these days is at least minDrop. Defaults: downDays = 3, minDrop = 0 (0.02 means 2 %).",
                "Sell when the close is higher than the close of the bar before.",
                "In a crash the price can go down many more days in a row. Trades are short, so fees matter."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("downDays", "Down bars in a row", ParameterType.Integer, 3, 2, 7, 1, "Number of closes lower than the close before."),
                new ParameterDefinition("minDrop", "Minimum total drop (fraction)", ParameterType.Decimal, 0, 0, 0.1, 0.005, "0.02 means the price must have lost at least 2 %."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int downDays = ReadIntParameter(parameters, "downDays");
        double minDrop = ReadParameter(parameters, "minDrop");
        double[] closes = GetCloses(bars);

        Signal[] signals = new Signal[bars.Count];
        for (int i = 1; i < bars.Count; i++)
        {
            // Sell: the close went up
            if (closes[i] > closes[i - 1])
            {
                signals[i] = Signal.Sell;
            }
            else if (i >= downDays && IsDownStreak(closes, i, downDays))
            {
                // total fall over the streak, as a fraction (-0.05 = -5 %)
                double drop = closes[i] / closes[i - downDays] - 1;
                if (drop <= -minDrop)
                {
                    signals[i] = Signal.Buy;
                }
            }
        }
        return signals;
    }

    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        // no line to draw for this strategy
        return new IndicatorSeries[0];
    }

    // true if the "downDays" last closes (up to bar i) are each lower than the close before
    private static bool IsDownStreak(double[] closes, int i, int downDays)
    {
        for (int k = 0; k < downDays; k++)
        {
            if (closes[i - k] >= closes[i - k - 1])
            {
                return false;
            }
        }
        return true;
    }
}
