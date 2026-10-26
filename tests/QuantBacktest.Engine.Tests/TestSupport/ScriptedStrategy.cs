using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine.Tests.TestSupport;

/// <summary>
/// A strategy that returns signals decided in advance by the test. It writes no logic of its own, so a test
/// checks the engine and nothing else (docs/parts/PART-3.md, section 3).
/// </summary>
internal sealed class ScriptedStrategy : IStrategy
{
    private readonly IReadOnlyList<Signal> _signals;

    public ScriptedStrategy(
        IReadOnlyList<Signal> signals,
        IReadOnlyList<ParameterDefinition>? parameters = null,
        IReadOnlyList<Timeframe>? timeframes = null)
    {
        _signals = signals;
        Parameters = parameters ?? new List<ParameterDefinition>();
        SupportedTimeframes = timeframes ?? new List<Timeframe> { Timeframe.Daily, Timeframe.Weekly };
    }

    /// <summary>Builds a strategy from letters: B = Buy, S = Sell, H = Hold ("BHSHHH").</summary>
    public static ScriptedStrategy FromLetters(string letters)
    {
        List<Signal> signals = new();
        foreach (char letter in letters)
        {
            signals.Add(letter switch
            {
                'B' => Signal.Buy,
                'S' => Signal.Sell,
                _ => Signal.Hold,
            });
        }

        return new ScriptedStrategy(signals);
    }

    /// <summary>When set, makes GenerateSignals throw (for example to imitate a violated relation between parameters).</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>When set, ComputeIndicators returns these series (full length, one value per bar).</summary>
    public IReadOnlyList<IndicatorSeries> Indicators { get; set; } = new List<IndicatorSeries>();

    public string Id { get; set; } = "scripted";

    public string Name { get; set; } = "Scripted strategy";

    public string Description => "Test strategy.";

    public StrategyCategory Category => StrategyCategory.Baseline;

    public StrategyDocumentation Documentation => new("idea", "entry", "exit", "risks");

    public IReadOnlyList<Timeframe> SupportedTimeframes { get; }

    public IReadOnlyList<ParameterDefinition> Parameters { get; }

    public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        // the engine passes bars[0..last]: give back one signal per bar, Hold after the end of the script
        List<Signal> result = new();
        for (int i = 0; i < bars.Count; i++)
        {
            result.Add(i < _signals.Count ? _signals[i] : Signal.Hold);
        }

        return result;
    }

    public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        return Indicators;
    }
}
