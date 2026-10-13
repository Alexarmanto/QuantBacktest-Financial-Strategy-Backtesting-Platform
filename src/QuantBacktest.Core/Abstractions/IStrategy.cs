using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>
/// Part 2. A strategy is a pure, deterministic function of prices and parameters.
/// It holds no state between calls and never touches the network, the clock, files or randomness.
/// </summary>
public interface IStrategy
{
    /// <summary>Stable kebab-case identifier, unique across the catalog (for example "sma-crossover").</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>One or two English sentences.</summary>
    string Description { get; }

    StrategyCategory Category { get; }

    StrategyDocumentation Documentation { get; }

    /// <summary>Timeframes this strategy accepts. Others are rejected by the backtest service.</summary>
    IReadOnlyList<Timeframe> SupportedTimeframes { get; }

    /// <summary>Tunable parameters. Empty for buy-and-hold and for custom (no-code) strategies.</summary>
    IReadOnlyList<ParameterDefinition> Parameters { get; }

    /// <summary>
    /// Returns exactly one signal per bar. signals[i] is emitted at the close of bar i and may only depend on
    /// bars[0..i] (no look-ahead). Hold during indicator warm-up. Missing parameter keys fall back to defaults.
    /// Throws <see cref="Errors.BacktestValidationException"/> for invalid relations between parameters.
    /// </summary>
    IReadOnlyList<Signal> GenerateSignals(
        IReadOnlyList<PriceBar> bars,
        IReadOnlyDictionary<string, double> parameters);

    /// <summary>
    /// The indicators this strategy uses, one value per bar (null during warm-up), for display only.
    /// Same purity and look-ahead rules as GenerateSignals. May return an empty list.
    /// </summary>
    IReadOnlyList<IndicatorSeries> ComputeIndicators(
        IReadOnlyList<PriceBar> bars,
        IReadOnlyDictionary<string, double> parameters);
}
