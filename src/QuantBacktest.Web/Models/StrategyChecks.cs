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
}
