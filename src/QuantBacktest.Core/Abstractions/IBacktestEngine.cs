using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 3. Pure simulation: no I/O. Same inputs always give the same result.</summary>
public interface IBacktestEngine
{
    /// <summary>
    /// Runs the simulation on an already-loaded series with an already-resolved strategy and validated parameters.
    /// Throws <see cref="Errors.BacktestValidationException"/> when the request cannot be simulated.
    /// </summary>
    BacktestResult Run(BacktestRequest request, PriceSeries series, IStrategy strategy);
}
