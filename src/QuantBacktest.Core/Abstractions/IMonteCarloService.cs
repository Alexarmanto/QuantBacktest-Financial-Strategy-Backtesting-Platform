using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 2 (QuantBacktest.Analytics). Pure and deterministic for a given seed.</summary>
public interface IMonteCarloService
{
    /// <summary>
    /// Bootstrap of the trade returns of a finished backtest (method: docs/CONTRACT.md section 9).
    /// Throws <see cref="Errors.BacktestValidationException"/> when there are fewer than
    /// Conventions.MinTradesForMonteCarlo trades or when the request is out of range.
    /// </summary>
    MonteCarloResult Run(BacktestResult result, MonteCarloRequest request);
}
