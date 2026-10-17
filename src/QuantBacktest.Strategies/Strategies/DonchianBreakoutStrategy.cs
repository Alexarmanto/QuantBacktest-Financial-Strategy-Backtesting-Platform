using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S6: buy when the price breaks its recent highest point, sell when it breaks its recent lowest point
internal class DonchianBreakoutStrategy : StrategyBase
{
    // Constructor
    public DonchianBreakoutStrategy()
        : base(
            "donchian-breakout",
            "Donchian channel breakout",
            "Buys when the price closes above its highest high of the last bars and sells when it closes below its lowest low.",
            StrategyCategory.Breakout,
            new StrategyDocumentation(
                "When the price breaks above the highest point of the last weeks, a new upward move often starts. The strategy enters on this breakout and leaves when the price breaks its recent low.",
                "Buy when the close is above the highest high of the previous entryLookback bars x (1 + threshold). Defaults: entryLookback = 20, threshold = 0.",
                "Sell when the close is below the lowest low of the previous exitLookback bars. Default: exitLookback = 10.",
                "Many breakouts fail and the price falls back at once (false breakouts). The strategy buys after the price has already risen."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("entryLookback", "Entry channel (bars)", ParameterType.Integer, 20, 5, 250, 1, "Bars used for the highest high."),
                new ParameterDefinition("exitLookback", "Exit channel (bars)", ParameterType.Integer, 10, 3, 250, 1, "Bars used for the lowest low."),
                new ParameterDefinition("threshold", "Breakout margin (fraction)", ParameterType.Decimal, 0, 0, 0.1, 0.001, "0.01 means the close must be 1 % above the highest high."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int entryLookback = ReadIntParameter(parameters, "entryLookback");
        int exitLookback = ReadIntParameter(parameters, "exitLookback");
        double threshold = ReadParameter(parameters, "threshold");

        double[] closes = GetCloses(bars);
        double[] highestHigh = Indicators.HighestHigh(GetHighs(bars), entryLookback);
        double[] lowestLow = Indicators.LowestLow(GetLows(bars), exitLookback);

        // a comparison with NaN (warm-up) is always false, so the signal stays Hold
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            bool buy = closes[i] > highestHigh[i] * (1 + threshold);
            bool sell = closes[i] < lowestLow[i];

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
        int entryLookback = ReadIntParameter(parameters, "entryLookback");
        int exitLookback = ReadIntParameter(parameters, "exitLookback");

        return new IndicatorSeries[]
        {
            MakeSeries("highest-high-" + entryLookback, "Highest high(" + entryLookback + ")", IndicatorPane.Price, Indicators.HighestHigh(GetHighs(bars), entryLookback), new double[0]),
            MakeSeries("lowest-low-" + exitLookback, "Lowest low(" + exitLookback + ")", IndicatorPane.Price, Indicators.LowestLow(GetLows(bars), exitLookback), new double[0]),
        };
    }
}
