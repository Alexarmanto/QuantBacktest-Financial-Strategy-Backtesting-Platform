using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 2. Turns a no-code definition into a runnable strategy.</summary>
public interface ICustomStrategyFactory
{
    /// <summary>
    /// Every problem of the definition, as English messages safe to display (empty list = valid).
    /// Rules: docs/CONTRACT.md section 8.
    /// </summary>
    IReadOnlyList<string> Validate(CustomStrategyDefinition definition);

    /// <summary>Throws <see cref="Errors.BacktestValidationException"/> (all problems joined) when Validate is not empty.</summary>
    IStrategy Create(CustomStrategyDefinition definition);
}
