using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The checks the web makes on the strategy of a request before running it, with their messages, shared by the
/// backtest form (P4-U1) and the comparator (P4-X1) so the two pages never word them differently.
/// </summary>
internal static class StrategyChecks
{
    /// <summary>IStrategyCatalog.FindAsync returned null for this id.</summary>
    public static string UnknownStrategy(string id) => $"Unknown strategy '{id}'.";

    /// <summary>The message when the strategy does not support the timeframe; null when it does.</summary>
    public static string? TimeframeError(IStrategy strategy, Timeframe timeframe)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return strategy.SupportedTimeframes.Contains(timeframe)
            ? null
            : $"{strategy.Name} does not support the {BacktestQuery.FormatTimeframe(timeframe)} timeframe "
                + $"({StrategyOption.TimeframeRestriction(strategy)}).";
    }

    /// <summary>Why a strategy cannot be optimised, shown next to its name in the heatmap form (O1, P4-U7).</summary>
    public const string TooFewParametersRestriction = "fewer than 2 parameters";

    /// <summary>
    /// O1: a heatmap needs at least 2 parameters (ResultActionsViewModel.MinParametersToOptimize, the same threshold
    /// as the "Optimize this strategy" button). The message when it has fewer; null when it can be optimised.
    /// </summary>
    public static string? OptimizationError(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return strategy.Parameters.Count >= ResultActionsViewModel.MinParametersToOptimize
            ? null
            : $"{strategy.Name} has {TooFewParametersRestriction}: a heatmap needs one for X and one for Y.";
    }
}
