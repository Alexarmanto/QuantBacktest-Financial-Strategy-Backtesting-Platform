using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S7: buy only when Bollinger AND RSI both say "too low"
internal class BollingerRsiConfirmationStrategy : StrategyBase
{
    // Constructor
    public BollingerRsiConfirmationStrategy()
        : base(
            "bollinger-rsi-confirmation",
            "Bollinger + RSI double confirmation",
            "Buys only when the price is below the lower Bollinger band and the RSI is oversold at the same time.",
            StrategyCategory.MeanReversion,
            new StrategyDocumentation(
                "Two different indicators must agree before buying, which removes some false signals. The strategy bets on a bounce after a strong fall.",
                "Buy when the close is below the lower band AND RSI(rsiPeriod) is below oversold. Defaults: period = 20, stdDevs = 2, rsiPeriod = 14, oversold = 30.",
                "Sell when the close is above the middle band OR RSI(rsiPeriod) is above overbought. Default: overbought = 70.",
                "Signals are rare, so there are few trades. In a crash both indicators can stay low while the price keeps falling."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("period", "Bollinger period (bars)", ParameterType.Integer, 20, 5, 100, 1, "Period of the moving average and of the standard deviation."),
                new ParameterDefinition("stdDevs", "Width (standard deviations)", ParameterType.Decimal, 2, 0.5, 4, 0.1, "Distance between the middle band and the outer bands."),
                new ParameterDefinition("rsiPeriod", "RSI period (bars)", ParameterType.Integer, 14, 2, 50, 1, "Number of bars used by the RSI."),
                new ParameterDefinition("oversold", "Oversold level", ParameterType.Decimal, 30, 5, 50, 1, "The RSI must be below this level to buy."),
                new ParameterDefinition("overbought", "Overbought level", ParameterType.Decimal, 70, 50, 95, 1, "Sell when the RSI is above this level."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int period = ReadIntParameter(parameters, "period");
        double stdDevs = ReadParameter(parameters, "stdDevs");
        int rsiPeriod = ReadIntParameter(parameters, "rsiPeriod");
        double oversold = ReadParameter(parameters, "oversold");
        double overbought = ReadParameter(parameters, "overbought");
        CheckRelation(oversold < overbought, "The oversold level must be smaller than the overbought level.");

        double[] closes = GetCloses(bars);
        BollingerBands bands = Indicators.Bollinger(closes, period, stdDevs);
        double[] rsi = Indicators.Rsi(closes, rsiPeriod);

        // a comparison with NaN (warm-up) is always false, so the signal stays Hold
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            bool buy = closes[i] < bands.Lower[i] && rsi[i] < oversold;
            bool sell = closes[i] > bands.Middle[i] || rsi[i] > overbought;

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
        int period = ReadIntParameter(parameters, "period");
        double stdDevs = ReadParameter(parameters, "stdDevs");
        int rsiPeriod = ReadIntParameter(parameters, "rsiPeriod");
        double oversold = ReadParameter(parameters, "oversold");
        double overbought = ReadParameter(parameters, "overbought");
        CheckRelation(oversold < overbought, "The oversold level must be smaller than the overbought level.");

        double[] closes = GetCloses(bars);
        BollingerBands bands = Indicators.Bollinger(closes, period, stdDevs);
        double[] rsi = Indicators.Rsi(closes, rsiPeriod);

        string end = period + "-" + Format(stdDevs);
        string label = "(" + period + ", " + Format(stdDevs) + ")";
        return new IndicatorSeries[]
        {
            MakeSeries("bb-upper-" + end, "BB upper" + label, IndicatorPane.Price, bands.Upper, new double[0]),
            MakeSeries("bb-middle-" + end, "BB middle" + label, IndicatorPane.Price, bands.Middle, new double[0]),
            MakeSeries("bb-lower-" + end, "BB lower" + label, IndicatorPane.Price, bands.Lower, new double[0]),
            MakeSeries("rsi-" + rsiPeriod, "RSI(" + rsiPeriod + ")", IndicatorPane.Oscillator, rsi, new double[] { oversold, overbought }),
        };
    }
}
