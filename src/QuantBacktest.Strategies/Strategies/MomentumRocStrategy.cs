using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S4: buy when the rate of change goes above a threshold, sell when it goes back below
internal class MomentumRocStrategy : StrategyBase
{
    // Constructor
    public MomentumRocStrategy()
        : base(
            "momentum-roc",
            "Momentum (rate of change)",
            "Buys when the price has risen enough over the last bars and sells when this momentum fades.",
            StrategyCategory.Momentum,
            new StrategyDocumentation(
                "A stock that has gone up recently tends to keep going up for a while. The strategy follows this momentum.",
                "Buy when ROC(lookback) crosses above threshold. Defaults: lookback = 14, threshold = 0 (0.05 means 5 %).",
                "Sell when ROC(lookback) crosses below threshold.",
                "When the trend suddenly reverses the strategy exits late. In a choppy market the ROC goes up and down around the threshold and gives false signals."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("lookback", "Lookback (bars)", ParameterType.Integer, 14, 2, 250, 1, "The price is compared with the price this many bars ago."),
                new ParameterDefinition("threshold", "Threshold (fraction)", ParameterType.Decimal, 0, 0, 0.5, 0.005, "0.05 means 5 %."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int lookback = ReadIntParameter(parameters, "lookback");
        double threshold = ReadParameter(parameters, "threshold");

        double[] roc = Indicators.Roc(GetCloses(bars), lookback);

        // the threshold as a line, to use CrossesAbove and CrossesBelow
        double[] thresholdLine = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            thresholdLine[i] = threshold;
        }

        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            if (Indicators.CrossesBelow(roc, thresholdLine, i))
            {
                signals[i] = Signal.Sell;
            }
            else if (Indicators.CrossesAbove(roc, thresholdLine, i))
            {
                signals[i] = Signal.Buy;
            }
        }
        return signals;
    }

    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int lookback = ReadIntParameter(parameters, "lookback");
        double threshold = ReadParameter(parameters, "threshold");

        double[] roc = Indicators.Roc(GetCloses(bars), lookback);
        return new IndicatorSeries[]
        {
            MakeSeries("roc-" + lookback, "ROC(" + lookback + ")", IndicatorPane.Oscillator, roc, new double[] { threshold }),
        };
    }
}
