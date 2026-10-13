using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 1. Persists the no-code strategies created in the UI (SQLite). No validation of rules here.</summary>
public interface ICustomStrategyStore
{
    /// <summary>All saved definitions, sorted by Name (ordinal, case-insensitive).</summary>
    Task<IReadOnlyList<CustomStrategyDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Null when the id is unknown.</summary>
    Task<CustomStrategyDefinition?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Inserts, or replaces the definition with the same Id.</summary>
    Task SaveAsync(CustomStrategyDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the id was unknown.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
