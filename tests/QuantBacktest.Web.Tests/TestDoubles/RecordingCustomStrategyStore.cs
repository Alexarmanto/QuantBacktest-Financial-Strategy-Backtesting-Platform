using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>Delegates to another store and records the ids passed to DeleteAsync.</summary>
internal sealed class RecordingCustomStrategyStore(ICustomStrategyStore inner) : ICustomStrategyStore
{
    public List<string> DeletedIds { get; } = [];

    public Task<IReadOnlyList<CustomStrategyDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        inner.GetAllAsync(cancellationToken);

    public Task<CustomStrategyDefinition?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        inner.GetAsync(id, cancellationToken);

    public Task SaveAsync(CustomStrategyDefinition definition, CancellationToken cancellationToken = default) =>
        inner.SaveAsync(definition, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        DeletedIds.Add(id);
        return inner.DeleteAsync(id, cancellationToken);
    }
}
