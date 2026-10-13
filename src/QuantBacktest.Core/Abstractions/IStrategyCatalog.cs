namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 2. Built-in strategies (discovered by reflection) plus the custom strategies of ICustomStrategyStore.</summary>
public interface IStrategyCatalog
{
    /// <summary>Built-in strategies sorted by Name, then custom strategies sorted by Name.</summary>
    Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Exact, case-sensitive id match. Null when unknown.</summary>
    Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default);
}
