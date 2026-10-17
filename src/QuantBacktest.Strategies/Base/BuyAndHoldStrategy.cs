using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-S11: the reference strategy, buy at the start and keep until the end
internal class BuyAndHoldStrategy : StrategyBase
{
    // Constructor
    public BuyAndHoldStrategy()
        : base(
            "buy-and-hold",
            "Buy and hold (baseline)",
            "Buys at the start and keeps the position until the end. Used as a reference to judge the other strategies.",
            StrategyCategory.Baseline,
            new StrategyDocumentation(
                "Markets tend to rise over the long term. Buying once and doing nothing avoids fees and timing mistakes. Every active strategy should try to do better than this.",
                "Buy on the first bar.",
                "Never sells: the position is closed at the end of the period.",
                "Takes every market crash in full, with no protection."),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[0])
    {
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        // Buy on every bar: the engine ignores a Buy when we already own the stock
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            signals[i] = Signal.Buy;
        }
        return signals;
    }

    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        return new IndicatorSeries[0];
    }
}
