using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine.Tests.TestSupport;

/// <summary>
/// A test strategy with two parameters: "fast" (Integer, [2, 30]) and "slow" (Integer, [3, 60]).
/// It buys every "slow" bars and sells every "fast" bars, and refuses fast >= slow like a real strategy (S5).
/// </summary>
internal sealed class TwoParameterStrategy : IStrategy
{
    public string Id => "two-parameters";

    public string Name => "Two parameters";

    public string Description => "Test strategy.";

    public StrategyCategory Category => StrategyCategory.Baseline;

    public StrategyDocumentation Documentation => new("idea", "entry", "exit", "risks");

    public IReadOnlyList<Timeframe> SupportedTimeframes => new List<Timeframe> { Timeframe.Daily, Timeframe.Weekly };

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = new List<ParameterDefinition>
    {
        new("fast", "Fast", ParameterType.Integer, 5, 2, 30, 1),
        new("slow", "Slow", ParameterType.Integer, 10, 3, 60, 1),
    };

    public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        int fast = (int)parameters["fast"];
        int slow = (int)parameters["slow"];
        if (fast >= slow)
        {
            throw new BacktestValidationException("fast must be lower than slow.");
        }

        List<Signal> signals = new();
        for (int i = 0; i < bars.Count; i++)
        {
            if (i % slow == 0)
            {
                signals.Add(Signal.Buy);
            }
            else if (i % fast == 0)
            {
                signals.Add(Signal.Sell);
            }
            else
            {
                signals.Add(Signal.Hold);
            }
        }

        return signals;
    }

    public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        return new List<IndicatorSeries>();
    }
}
