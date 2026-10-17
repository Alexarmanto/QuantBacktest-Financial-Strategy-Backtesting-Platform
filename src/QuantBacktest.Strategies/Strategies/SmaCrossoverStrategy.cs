using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S1: buy when the fast average crosses above the slow one, sell when it crosses below
internal class SmaCrossoverStrategy : StrategyBase
{
    // Constructor
    public SmaCrossoverStrategy()
        : base(
            "sma-crossover",
            "Moving average crossover",
            "Follows the trend: buys when a fast moving average crosses above a slow one and sells when it crosses back below.",
            StrategyCategory.TrendFollowing,
            new StrategyDocumentation(
                "When the recent average price goes above the longer average, the trend is turning up. The strategy tries to ride the trend and leaves when it turns down.",
                "Buy when SMA(fast) crosses above SMA(slow). Defaults: fast = 20, slow = 50.",
                "Sell when SMA(fast) crosses below SMA(slow).",
                "Averages are late, so the strategy enters and exits after the move has started. In a sideways market the averages cross often, which gives many small losses and fees."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("fast", "Fast SMA (bars)", ParameterType.Integer, 20, 2, 200, 1, "Period of the fast moving average."),
                new ParameterDefinition("slow", "Slow SMA (bars)", ParameterType.Integer, 50, 5, 400, 1, "Period of the slow moving average."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int fast = ReadIntParameter(parameters, "fast");
        int slow = ReadIntParameter(parameters, "slow");
        CheckRelation(fast < slow, "The fast period must be smaller than the slow period.");

        double[] closes = GetCloses(bars);
        double[] fastSma = Indicators.Sma(closes, fast);
        double[] slowSma = Indicators.Sma(closes, slow);

        // all signals are Hold at the start
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            if (Indicators.CrossesBelow(fastSma, slowSma, i))
            {
                signals[i] = Signal.Sell;
            }
            else if (Indicators.CrossesAbove(fastSma, slowSma, i))
            {
                signals[i] = Signal.Buy;
            }
        }
        return signals;
    }

    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int fast = ReadIntParameter(parameters, "fast");
        int slow = ReadIntParameter(parameters, "slow");
        CheckRelation(fast < slow, "The fast period must be smaller than the slow period.");

        double[] closes = GetCloses(bars);
        return new IndicatorSeries[]
        {
            MakeSeries("sma-" + fast, "SMA(" + fast + ")", IndicatorPane.Price, Indicators.Sma(closes, fast), new double[0]),
            MakeSeries("sma-" + slow, "SMA(" + slow + ")", IndicatorPane.Price, Indicators.Sma(closes, slow), new double[0]),
        };
    }
}
