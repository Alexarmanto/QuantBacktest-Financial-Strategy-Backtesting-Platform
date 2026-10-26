using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>One input of a strategy parameter, built from its ParameterDefinition (P4-U1). Name is the URL key ("p.fast").</summary>
internal sealed record ParameterField(
    string Name,
    string Label,
    string Value,
    string Min,
    string Max,
    string Step,
    string? Description,
    string? Error);

/// <summary>One entry of the strategy list and the fieldset of its parameters. Everything comes from IStrategyCatalog.</summary>
internal sealed record StrategyOption(
    string Id,
    string Name,
    string Timeframes,
    string? Restriction,
    bool IsSelected,
    IReadOnlyList<ParameterField> Parameters)
{
    private static readonly Timeframe[] AllTimeframes = Enum.GetValues<Timeframe>();

    /// <summary>Supported timeframes in URL spelling, separated by spaces ("daily weekly"), read by backtest-form.js.</summary>
    public static string FormatTimeframes(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return string.Join(' ', strategy.SupportedTimeframes.Order().Select(BacktestQuery.FormatTimeframe));
    }

    /// <summary>Why the strategy is not available everywhere ("daily only"); null when it supports every timeframe.</summary>
    public static string? TimeframeRestriction(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (AllTimeframes.All(strategy.SupportedTimeframes.Contains))
        {
            return null;
        }

        return string.Join(" and ", strategy.SupportedTimeframes.Order().Select(BacktestQuery.FormatTimeframe)) + " only";
    }

    /// <summary>
    /// Parameter inputs of one strategy: the raw value from the URL when this strategy is the selected one and the
    /// key is present, the catalog default otherwise.
    /// </summary>
    public static IReadOnlyList<ParameterField> CreateFields(
        IStrategy strategy,
        Func<string, string?> rawValue,
        Func<string, string?> error)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return strategy.Parameters
            .Select(definition =>
            {
                var name = BacktestQuery.ParameterPrefix + definition.Key;
                return new ParameterField(
                    name,
                    definition.Label,
                    rawValue(name) ?? BacktestQuery.FormatNumber(definition.DefaultValue),
                    BacktestQuery.FormatNumber(definition.Min),
                    BacktestQuery.FormatNumber(definition.Max),
                    BacktestQuery.FormatNumber(definition.Step),
                    definition.Description,
                    error(name));
            })
            .ToList();
    }
}
