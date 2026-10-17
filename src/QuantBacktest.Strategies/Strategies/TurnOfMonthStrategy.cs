using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S10: buy just before the end of the month, sell a few days after the start of the next one
internal class TurnOfMonthStrategy : StrategyBase
{
    // Constructor
    public TurnOfMonthStrategy()
        : base(
            "turn-of-month",
            "Turn-of-the-month effect",
            "Buys a few business days before the end of the month and sells a few bars after the start of the next month.",
            StrategyCategory.Calendar,
            new StrategyDocumentation(
                "Stocks often rise around the turn of the month, when salaries and funds are invested. The strategy is only in the market during these few days.",
                "Buy when there are exactly daysBeforeEnd business days (Monday to Friday) left after this bar until the end of the month. Default: daysBeforeEnd = 1. Bank holidays are not known, so this is an approximation.",
                "Sell on the daysAfterStart-th bar of the month. Default: daysAfterStart = 3.",
                "The effect is small and can disappear for many months. Two trades per month means fees matter a lot."),
            new Timeframe[] { Timeframe.Daily },
            new ParameterDefinition[]
            {
                new ParameterDefinition("daysBeforeEnd", "Business days before month end", ParameterType.Integer, 1, 1, 5, 1, "Buy when this many business days are left in the month."),
                new ParameterDefinition("daysAfterStart", "Bars after month start", ParameterType.Integer, 3, 1, 10, 1, "Sell on this bar of the new month."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int daysBeforeEnd = ReadIntParameter(parameters, "daysBeforeEnd");
        int daysAfterStart = ReadIntParameter(parameters, "daysAfterStart");

        Signal[] signals = new Signal[bars.Count];
        int barNumberInMonth = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            // count the bars of the current month (only bars up to i, no look-ahead)
            if (i > 0 && bars[i].Date.Month == bars[i - 1].Date.Month && bars[i].Date.Year == bars[i - 1].Date.Year)
            {
                barNumberInMonth++;
            }
            else
            {
                barNumberInMonth = 1;
            }

            bool buy = BusinessDaysLeftInMonth(bars[i].Date) == daysBeforeEnd;
            bool sell = barNumberInMonth == daysAfterStart;

            // S4: if both are true, Sell wins
            if (sell)
            {
                signals[i] = Signal.Sell;
            }
            else if (buy)
            {
                signals[i] = Signal.Buy;
            }
        }
        return signals;
    }

    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        // no line to draw for this strategy
        return new IndicatorSeries[0];
    }

    // number of days from Monday to Friday strictly after "date" and until the last day of its month
    private static int BusinessDaysLeftInMonth(DateOnly date)
    {
        int count = 0;
        DateOnly day = date.AddDays(1);
        while (day.Month == date.Month)
        {
            if (day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday)
            {
                count++;
            }
            day = day.AddDays(1);
        }
        return count;
    }
}
