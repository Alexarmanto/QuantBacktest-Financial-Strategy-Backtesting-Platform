using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S2: buy when the price falls below the lower Bollinger band, sell when it comes back up
internal class BollingerReversionStrategy : StrategyBase
{
    // Constructor
    public BollingerReversionStrategy()
        : base(
            "bollinger-reversion",
            "Bollinger mean reversion",
            "Buys when the price falls below the lower Bollinger band and sells when it comes back to the middle or upper band.",
            StrategyCategory.MeanReversion,
            new StrategyDocumentation(
                "A price far below its average is unusually low. The strategy bets that it will come back towards the average.",
                "Buy when the close falls below the lower band (it was above or on it the bar before). Defaults: period = 20, stdDevs = 2.",
                "Sell when the close crosses above the middle band (exitLevel = 1, default) or the upper band (exitLevel = 0).",
                "In a strong downtrend the price can stay below the band and keep falling, so the strategy buys too early and loses."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("period", "Period (bars)", ParameterType.Integer, 20, 5, 100, 1, "Period of the moving average and of the standard deviation."),
                new ParameterDefinition("stdDevs", "Width (standard deviations)", ParameterType.Decimal, 2, 0.5, 4, 0.1, "Distance between the middle band and the outer bands."),
                new ParameterDefinition("exitLevel", "Exit band (1 = middle, 0 = upper)", ParameterType.Integer, 1, 0, 1, 1, "Band that triggers the sale."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int period = ReadIntParameter(parameters, "period");
        double stdDevs = ReadParameter(parameters, "stdDevs");
        int exitLevel = ReadIntParameter(parameters, "exitLevel");

        double[] closes = GetCloses(bars);
        BollingerBands bands = Indicators.Bollinger(closes, period, stdDevs);

        // exit on the middle band (1) or on the upper band (0)
        double[] exitBand = bands.Upper;
        if (exitLevel == 1)
        {
            exitBand = bands.Middle;
        }

        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            // close[i] < lower[i] and close[i-1] >= lower[i-1] is exactly "crosses below"
            bool buy = Indicators.CrossesBelow(closes, bands.Lower, i);
            bool sell = Indicators.CrossesAbove(closes, exitBand, i);

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
        BollingerBands bands = Indicators.Bollinger(GetCloses(bars), period, stdDevs);

        string end = period + "-" + Format(stdDevs);
        string label = "(" + period + ", " + Format(stdDevs) + ")";
        return new IndicatorSeries[]
        {
            MakeSeries("bb-upper-" + end, "BB upper" + label, IndicatorPane.Price, bands.Upper, new double[0]),
            MakeSeries("bb-middle-" + end, "BB middle" + label, IndicatorPane.Price, bands.Middle, new double[0]),
            MakeSeries("bb-lower-" + end, "BB lower" + label, IndicatorPane.Price, bands.Lower, new double[0]),
        };
    }
}
