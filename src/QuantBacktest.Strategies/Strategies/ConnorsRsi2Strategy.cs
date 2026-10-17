using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S8: in an uptrend, buy a short dip (very low RSI-2) and sell on the first small rebound
internal class ConnorsRsi2Strategy : StrategyBase
{
    // Constructor
    public ConnorsRsi2Strategy()
        : base(
            "connors-rsi2",
            "Trend-filtered RSI-2 (Connors)",
            "In a long-term uptrend, buys short dips spotted by a very short RSI and sells as soon as the price bounces back.",
            StrategyCategory.MeanReversion,
            new StrategyDocumentation(
                "When the long-term trend is up, short dips are often bought back quickly. The long average checks the trend and a 2-bar RSI finds the dips.",
                "Buy when the close is above SMA(trendPeriod) AND RSI(rsiPeriod) is below entryThreshold. Defaults: trendPeriod = 200, rsiPeriod = 2, entryThreshold = 10.",
                "Sell when the close is above SMA(exitPeriod). Default: exitPeriod = 5.",
                "If the trend breaks, a small dip can become a big fall. Trades are short and frequent, so fees matter a lot."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("trendPeriod", "Trend SMA (bars)", ParameterType.Integer, 200, 50, 300, 10, "The close must be above this average to buy."),
                new ParameterDefinition("rsiPeriod", "RSI period (bars)", ParameterType.Integer, 2, 2, 10, 1, "Number of bars used by the RSI."),
                new ParameterDefinition("entryThreshold", "RSI entry level", ParameterType.Decimal, 10, 1, 40, 1, "Buy when the RSI is below this level."),
                new ParameterDefinition("exitPeriod", "Exit SMA (bars)", ParameterType.Integer, 5, 2, 20, 1, "Sell when the close goes above this average."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int trendPeriod = ReadIntParameter(parameters, "trendPeriod");
        int rsiPeriod = ReadIntParameter(parameters, "rsiPeriod");
        double entryThreshold = ReadParameter(parameters, "entryThreshold");
        int exitPeriod = ReadIntParameter(parameters, "exitPeriod");
        CheckRelation(exitPeriod < trendPeriod, "The exit period must be smaller than the trend period.");

        double[] closes = GetCloses(bars);
        double[] trendSma = Indicators.Sma(closes, trendPeriod);
        double[] exitSma = Indicators.Sma(closes, exitPeriod);
        double[] rsi = Indicators.Rsi(closes, rsiPeriod);

        // a comparison with NaN (warm-up) is always false, so the signal stays Hold
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            bool buy = closes[i] > trendSma[i] && rsi[i] < entryThreshold;
            bool sell = closes[i] > exitSma[i];

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
        int trendPeriod = ReadIntParameter(parameters, "trendPeriod");
        int rsiPeriod = ReadIntParameter(parameters, "rsiPeriod");
        double entryThreshold = ReadParameter(parameters, "entryThreshold");
        int exitPeriod = ReadIntParameter(parameters, "exitPeriod");
        CheckRelation(exitPeriod < trendPeriod, "The exit period must be smaller than the trend period.");

        double[] closes = GetCloses(bars);
        return new IndicatorSeries[]
        {
            MakeSeries("sma-" + trendPeriod, "SMA(" + trendPeriod + ")", IndicatorPane.Price, Indicators.Sma(closes, trendPeriod), new double[0]),
            MakeSeries("sma-" + exitPeriod, "SMA(" + exitPeriod + ")", IndicatorPane.Price, Indicators.Sma(closes, exitPeriod), new double[0]),
            MakeSeries("rsi-" + rsiPeriod, "RSI(" + rsiPeriod + ")", IndicatorPane.Oscillator, Indicators.Rsi(closes, rsiPeriod), new double[] { entryThreshold }),
        };
    }
}
