using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 3. Runs one backtest per combination of two parameters (optimisation heatmap).</summary>
public interface IParameterSweepService
{
    /// <summary>
    /// Rules: docs/CONTRACT.md section 6. Throws <see cref="Errors.BacktestValidationException"/> for an invalid
    /// request (unknown key, same key twice, too many cells...). An invalid single combination is not an exception:
    /// it gives a cell with a null Value and an Error.
    /// </summary>
    Task<SweepResult> RunAsync(SweepRequest request, CancellationToken cancellationToken = default);
}
