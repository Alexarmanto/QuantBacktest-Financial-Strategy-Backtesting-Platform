using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S3: buy when the RSI says "oversold", sell when it says "overbought"
internal class RsiReversionStrategy : StrategyBase
{
    // Constructor
    public RsiReversionStrategy()
        : base(
            "rsi-reversion",
            "RSI mean reversion",
            "Buys when the RSI shows that the stock fell too much and sells when it shows that it rose too much.",
            StrategyCategory.MeanReversion,
            new StrategyDocumentation(
                "The RSI goes from 0 to 100 and measures the strength of the recent moves. A very low RSI means the stock fell a lot in a short time, so a bounce is likely.",
                "Buy when RSI(period) is below oversold. Defaults: period = 14, oversold = 30.",
                "Sell when RSI(period) is above overbought. Default: overbought = 70.",
                "In a long downtrend the RSI can stay low for a long time while the price keeps falling."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("period", "RSI period (bars)", ParameterType.Integer, 14, 2, 50, 1, "Number of bars used by the RSI."),
                new ParameterDefinition("oversold", "Oversold level", ParameterType.Decimal, 30, 5, 50, 1, "Buy below this RSI level."),
                new ParameterDefinition("overbought", "Overbought level", ParameterType.Decimal, 70, 50, 95, 1, "Sell above this RSI level."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int period = ReadIntParameter(parameters, "period");
        double oversold = ReadParameter(parameters, "oversold");
        double overbought = ReadParameter(parameters, "overbought");
        CheckRelation(oversold < overbought, "The oversold level must be smaller than the overbought level.");

        double[] rsi = Indicators.Rsi(GetCloses(bars), period);

        // a comparison with NaN (warm-up) is always false, so the signal stays Hold
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            if (rsi[i] > overbought)
            {
                signals[i] = Signal.Sell;
            }
            else if (rsi[i] < oversold)
            {
                signals[i] = Signal.Buy;
            }
        }
        return signals;
    }

    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int period = ReadIntParameter(parameters, "period");
        double oversold = ReadParameter(parameters, "oversold");
        double overbought = ReadParameter(parameters, "overbought");
        CheckRelation(oversold < overbought, "The oversold level must be smaller than the overbought level.");

        double[] rsi = Indicators.Rsi(GetCloses(bars), period);
        return new IndicatorSeries[]
        {
            MakeSeries("rsi-" + period, "RSI(" + period + ")", IndicatorPane.Oscillator, rsi, new double[] { oversold, overbought }),
        };
    }
}
