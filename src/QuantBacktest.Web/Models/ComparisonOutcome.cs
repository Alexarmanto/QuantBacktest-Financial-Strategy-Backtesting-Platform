using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// What happened to one strategy of a comparison (P4-X1): its result and the backtest query that produced it, or the
/// message of its error (C-5). StrategyName is the id itself when the catalog does not know the id.
/// </summary>
internal sealed record ComparisonOutcome(
    string StrategyId,
    string StrategyName,
    BacktestQuery? Query,
    BacktestResult? Result,
    string? Error)
{
    public bool Succeeded => Result is not null;

    public static ComparisonOutcome Success(IStrategy strategy, BacktestQuery query, BacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(result);
        return new(strategy.Id, strategy.Name, query, result, null);
    }

    public static ComparisonOutcome Failure(string strategyId, string strategyName, string message)
    {
        ArgumentNullException.ThrowIfNull(strategyId);
        ArgumentNullException.ThrowIfNull(strategyName);
        ArgumentNullException.ThrowIfNull(message);
        return new(strategyId, strategyName, null, null, message);
    }
}
