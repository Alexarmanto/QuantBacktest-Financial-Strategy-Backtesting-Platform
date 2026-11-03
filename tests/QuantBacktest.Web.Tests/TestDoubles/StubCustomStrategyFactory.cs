using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// The fake factory of the app plus the given problems (as the real Validate would return them), recording every
/// definition it validates. The optional log is shared with RecordingCustomStrategyStore.
/// </summary>
internal sealed class StubCustomStrategyFactory(IReadOnlyList<string>? extraProblems = null, List<string>? log = null)
    : ICustomStrategyFactory
{
    private readonly FakeCustomStrategyFactory _inner = new();

    public List<CustomStrategyDefinition> Validated { get; } = [];

    public IReadOnlyList<string> Validate(CustomStrategyDefinition definition)
    {
        Validated.Add(definition);
        log?.Add("Validate " + definition.Id);
        return [.. _inner.Validate(definition), .. extraProblems ?? []];
    }

    public IStrategy Create(CustomStrategyDefinition definition) => _inner.Create(definition);
}
