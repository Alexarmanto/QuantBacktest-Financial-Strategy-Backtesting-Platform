using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>
/// Two placeholder built-in strategies (one daily-only, both with 2 parameters for the heatmap)
/// plus the custom strategies of the store. NOT the real strategies of Part 2.
/// </summary>
public sealed class FakeStrategyCatalog(ICustomStrategyStore store, ICustomStrategyFactory factory) : IStrategyCatalog
{
    private readonly IReadOnlyList<IStrategy> _builtIns =
        new IStrategy[] { new FakeAlternatingStrategy(), new FakeThresholdStrategy() }
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await store.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var customs = definitions
            .Select(factory.Create)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase);
        return _builtIns.Concat(customs).ToList();
    }

    public async Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var builtIn = _builtIns.FirstOrDefault(s => s.Id == id);
        if (builtIn is not null)
        {
            return builtIn;
        }

        if (!id.StartsWith(Conventions.CustomStrategyIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var definition = await store.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return definition is null ? null : factory.Create(definition);
    }
}

/// <summary>Checks only the name, the id and the number of conditions. Not the rules R1 to R8 of the contract.</summary>
public sealed class FakeCustomStrategyFactory : ICustomStrategyFactory
{
    public IReadOnlyList<string> Validate(CustomStrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();
        var name = definition.Name.Trim();
        if (name.Length is < 1 or > 60)
        {
            errors.Add("The name must contain 1 to 60 characters.");
        }
        else
        {
            string? expectedId = null;
            try
            {
                expectedId = Conventions.CreateCustomStrategyId(name);
            }
            catch (ArgumentException)
            {
                errors.Add("The name must contain at least one letter or digit.");
            }

            if (expectedId is not null && expectedId != definition.Id)
            {
                errors.Add($"The id must be '{expectedId}'.");
            }
        }

        CheckRule("entry", definition.Entry, errors);
        CheckRule("exit", definition.Exit, errors);
        return errors;
    }

    public IStrategy Create(CustomStrategyDefinition definition)
    {
        var errors = Validate(definition);
        if (errors.Count > 0)
        {
            throw new BacktestValidationException(string.Join(" ", errors));
        }

        return new FakeCustomStrategy(definition);
    }

    private static void CheckRule(string label, RuleSet rule, List<string> errors)
    {
        if (rule.Conditions.Count is < 1 or > Conventions.MaxConditionsPerRule)
        {
            errors.Add($"The {label} rule must have 1 to {Conventions.MaxConditionsPerRule} conditions.");
        }
    }
}

internal static class FakeHelpers
{
    public static double Read(IReadOnlyDictionary<string, double> parameters, string key, double fallback)
        => parameters.TryGetValue(key, out var value) ? value : fallback;

    public static double?[] MovingAverage(IReadOnlyList<PriceBar> bars, int period)
    {
        var values = new double?[bars.Count];
        var sum = 0.0;
        for (var i = 0; i < bars.Count; i++)
        {
            sum += (double)bars[i].Close;
            if (i >= period)
            {
                sum -= (double)bars[i - period].Close;
            }

            values[i] = i >= period - 1 ? sum / period : null;
        }

        return values;
    }
}

internal sealed class FakeAlternatingStrategy : IStrategy
{
    public string Id => "fake-alternating";

    public string Name => "[FAKE] Alternating trades";

    public string Description => "Placeholder: buys and sells at fixed intervals. Daily only. Not a real strategy.";

    public StrategyCategory Category => StrategyCategory.Momentum;

    public StrategyDocumentation Documentation { get; } = new(
        "[FAKE] Trades on a fixed rhythm to exercise the UI.",
        "Buy every 2 x period bars.",
        "Sell period bars after each buy.",
        "Meaningless: placeholder only.");

    public IReadOnlyList<Timeframe> SupportedTimeframes { get; } = [Timeframe.Daily];

    public IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new ParameterDefinition("period", "Period (bars)", ParameterType.Integer, 20, 2, 200, 1, "Bars between a buy and the next sell."),
        new ParameterDefinition("offset", "Offset (bars)", ParameterType.Integer, 0, 0, 50, 1, "Shifts the rhythm."),
    ];

    public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        var period = Math.Max(2, (int)FakeHelpers.Read(parameters, "period", 20));
        var offset = Math.Max(0, (int)FakeHelpers.Read(parameters, "offset", 0));
        var signals = new Signal[bars.Count];
        for (var i = 1; i < bars.Count; i++)
        {
            var phase = (i + offset) % (2 * period);
            signals[i] = phase == 0 ? Signal.Buy : phase == period ? Signal.Sell : Signal.Hold;
        }

        return signals;
    }

    public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        var period = Math.Max(2, (int)FakeHelpers.Read(parameters, "period", 20));
        return [new IndicatorSeries($"sma-{period}", $"SMA({period})", IndicatorPane.Price, FakeHelpers.MovingAverage(bars, period), [])];
    }
}

internal sealed class FakeThresholdStrategy : IStrategy
{
    public string Id => "fake-threshold";

    public string Name => "[FAKE] Threshold on past return";

    public string Description => "Placeholder: buys after a rise above the threshold, sells after a fall. Not a real strategy.";

    public StrategyCategory Category => StrategyCategory.Momentum;

    public StrategyDocumentation Documentation { get; } = new(
        "[FAKE] Follows recent moves to exercise the UI.",
        "Buy when the return over the lookback is above the threshold.",
        "Sell when it is below minus the threshold.",
        "Meaningless: placeholder only.");

    public IReadOnlyList<Timeframe> SupportedTimeframes { get; } = [Timeframe.Daily, Timeframe.Weekly];

    public IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new ParameterDefinition("lookback", "Lookback (bars)", ParameterType.Integer, 10, 1, 100, 1),
        new ParameterDefinition("threshold", "Threshold (fraction)", ParameterType.Decimal, 0.02, 0.0, 0.5, 0.005, "0.02 means 2 %."),
    ];

    public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        var lookback = Math.Max(1, (int)FakeHelpers.Read(parameters, "lookback", 10));
        var threshold = FakeHelpers.Read(parameters, "threshold", 0.02);
        var signals = new Signal[bars.Count];
        for (var i = lookback; i < bars.Count; i++)
        {
            var change = (double)(bars[i].Close / bars[i - lookback].Close) - 1.0;
            signals[i] = change > threshold ? Signal.Buy : change < -threshold ? Signal.Sell : Signal.Hold;
        }

        return signals;
    }

    public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        var lookback = Math.Max(1, (int)FakeHelpers.Read(parameters, "lookback", 10));
        var threshold = FakeHelpers.Read(parameters, "threshold", 0.02);
        var values = new double?[bars.Count];
        for (var i = lookback; i < bars.Count; i++)
        {
            values[i] = (double)(bars[i].Close / bars[i - lookback].Close) - 1.0;
        }

        return [new IndicatorSeries($"roc-{lookback}", $"ROC({lookback})", IndicatorPane.Oscillator, values, [threshold, -threshold])];
    }
}

internal sealed class FakeCustomStrategy : IStrategy
{
    private readonly CustomStrategyDefinition _definition;

    public FakeCustomStrategy(CustomStrategyDefinition definition)
    {
        _definition = definition;
        Documentation = new StrategyDocumentation(
            definition.Description,
            $"[FAKE] {definition.Entry.Conditions.Count} entry condition(s), combined with {definition.Entry.Combinator}.",
            $"[FAKE] {definition.Exit.Conditions.Count} exit condition(s), combined with {definition.Exit.Combinator}.",
            "[FAKE] The rules are not evaluated by the fake: signals alternate every 15 bars.");
    }

    public string Id => _definition.Id;

    public string Name => _definition.Name;

    public string Description => _definition.Description;

    public StrategyCategory Category => StrategyCategory.Custom;

    public StrategyDocumentation Documentation { get; }

    public IReadOnlyList<Timeframe> SupportedTimeframes { get; } = [Timeframe.Daily, Timeframe.Weekly];

    public IReadOnlyList<ParameterDefinition> Parameters { get; } = [];

    public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        var signals = new Signal[bars.Count];
        for (var i = 1; i < bars.Count; i++)
        {
            var phase = i % 30;
            signals[i] = phase == 0 ? Signal.Buy : phase == 15 ? Signal.Sell : Signal.Hold;
        }

        return signals;
    }

    public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters) => [];
}
