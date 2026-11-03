using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// Delegates to another store and records the ids passed to DeleteAsync and the definitions passed to SaveAsync.
/// The optional log is shared with StubCustomStrategyFactory to check the order of the calls.
/// </summary>
internal sealed class RecordingCustomStrategyStore(ICustomStrategyStore inner, List<string>? log = null) : ICustomStrategyStore
{
    public List<string> DeletedIds { get; } = [];

    public List<CustomStrategyDefinition> SavedDefinitions { get; } = [];

    public Task<IReadOnlyList<CustomStrategyDefinition>> GetAllAsync(CancellationToken cancellationToken = default) =>
        inner.GetAllAsync(cancellationToken);

    public Task<CustomStrategyDefinition?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        inner.GetAsync(id, cancellationToken);

    public Task SaveAsync(CustomStrategyDefinition definition, CancellationToken cancellationToken = default)
    {
        SavedDefinitions.Add(definition);
        log?.Add("SaveAsync " + definition.Id);
        return inner.SaveAsync(definition, cancellationToken);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        DeletedIds.Add(id);
        return inner.DeleteAsync(id, cancellationToken);
    }
}
