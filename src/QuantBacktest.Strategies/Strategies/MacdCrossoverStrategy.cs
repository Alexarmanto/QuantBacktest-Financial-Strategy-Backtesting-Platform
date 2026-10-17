using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S5: buy when the MACD line crosses above its signal line, sell when it crosses below
internal class MacdCrossoverStrategy : StrategyBase
{
    // Constructor
    public MacdCrossoverStrategy()
        : base(
            "macd-crossover",
            "MACD crossover",
            "Buys when the MACD line crosses above its signal line and sells when it crosses back below.",
            StrategyCategory.TrendFollowing,
            new StrategyDocumentation(
                "The MACD line is the gap between a fast and a slow exponential average. When it rises above its own average (the signal line), the trend is getting stronger.",
                "Buy when the MACD line crosses above the signal line. Defaults: fast = 12, slow = 26, signal = 9.",
                "Sell when the MACD line crosses below the signal line.",
                "The MACD is late, like all moving averages. In a sideways market the two lines cross often and give losing trades."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[]
            {
                new ParameterDefinition("fast", "Fast EMA (bars)", ParameterType.Integer, 12, 2, 100, 1, "Period of the fast exponential average."),
                new ParameterDefinition("slow", "Slow EMA (bars)", ParameterType.Integer, 26, 5, 200, 1, "Period of the slow exponential average."),
                new ParameterDefinition("signal", "Signal EMA (bars)", ParameterType.Integer, 9, 2, 50, 1, "Period of the average of the MACD line."),
            })
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int fast = ReadIntParameter(parameters, "fast");
        int slow = ReadIntParameter(parameters, "slow");
        int signal = ReadIntParameter(parameters, "signal");
        CheckRelation(fast < slow, "The fast period must be smaller than the slow period.");

        MacdResult macd = Indicators.Macd(GetCloses(bars), fast, slow, signal);

        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            if (Indicators.CrossesBelow(macd.Line, macd.Signal, i))
            {
                signals[i] = Signal.Sell;
            }
            else if (Indicators.CrossesAbove(macd.Line, macd.Signal, i))
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
        int signal = ReadIntParameter(parameters, "signal");
        CheckRelation(fast < slow, "The fast period must be smaller than the slow period.");

        MacdResult macd = Indicators.Macd(GetCloses(bars), fast, slow, signal);
        string label = "(" + fast + ", " + slow + ", " + signal + ")";
        return new IndicatorSeries[]
        {
            MakeSeries("macd-line", "MACD line" + label, IndicatorPane.Oscillator, macd.Line, new double[] { 0 }),
            MakeSeries("macd-signal", "MACD signal" + label, IndicatorPane.Oscillator, macd.Signal, new double[] { 0 }),
        };
    }
}
