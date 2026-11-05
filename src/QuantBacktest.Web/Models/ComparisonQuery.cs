using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The settings of a comparison (P4-X1, C1) as they travel in its permalink: the settings shared by every strategy and
/// the ids, in the order of the URL. Built only by <see cref="BacktestQuery.TryParseComparison"/>; the URL is read and
/// written by BacktestQuery. The shared settings carry no strategy, so they never leave this class as they are:
/// <see cref="ForStrategy"/> is the only way to get a BacktestQuery out of it.
/// </summary>
internal sealed class ComparisonQuery
{
    private readonly BacktestQuery _settings;

    internal ComparisonQuery(BacktestQuery settings, IReadOnlyList<string> strategyIds)
    {
        _settings = settings;
        StrategyIds = strategyIds;
    }

    public IReadOnlyList<string> StrategyIds { get; }

    public string Symbol => _settings.Symbol;

    public Timeframe Timeframe => _settings.Timeframe;

    /// <summary>Execution settings of the contract (fractions), the same for every strategy (C1).</summary>
    public ExecutionSettings Execution => _settings.ToBacktestRequest().Execution;

    /// <summary>
    /// The backtest of one strategy: the shared settings, this strategy and every one of its parameters at the catalog
    /// default (P4-X1). Its ToQueryString() is the canonical URL of /Backtest/Result for the same request.
    /// </summary>
    public BacktestQuery ForStrategy(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return (_settings with
        {
            StrategyId = strategy.Id,
            Parameters = new Dictionary<string, decimal>(StringComparer.Ordinal),
        }).WithDefaultParameters(strategy.Parameters);
    }

    /// <summary>Canonical query string of the comparator, without the leading "?".</summary>
    public string ToQueryString() => _settings.ToComparisonQueryString(StrategyIds);

    public bool IsCanonical(QueryString received) => _settings.IsCanonicalComparison(received, StrategyIds);
}
