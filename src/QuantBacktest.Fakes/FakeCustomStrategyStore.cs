using System.Collections.Concurrent;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>In-memory store, pre-filled with one example definition. Lost when the application stops.</summary>
public sealed class FakeCustomStrategyStore : ICustomStrategyStore
{
    private readonly ConcurrentDictionary<string, CustomStrategyDefinition> _definitions = new(StringComparer.Ordinal);

    public FakeCustomStrategyStore()
    {
        var example = Example;
        _definitions[example.Id] = example;
    }

    /// <summary>"Close above SMA(200) and RSI(2) below 10" then "Close crosses above SMA(5)".</summary>
    public static CustomStrategyDefinition Example { get; } = new(
        "custom-trend-dip",
        "Trend dip",
        "[FAKE] Example saved strategy: buys a short dip inside a long uptrend.",
        new RuleSet(
            RuleCombinator.All,
            [
                new RuleCondition(new Operand(OperandKind.Close), ComparisonOperator.GreaterThan, new Operand(OperandKind.Sma, Period: 200)),
                new RuleCondition(new Operand(OperandKind.Rsi, Period: 2), ComparisonOperator.LessThan, new Operand(OperandKind.Constant, Value: 10)),
            ]),
        new RuleSet(
            RuleCombinator.Any,
            [
                new RuleCondition(new Operand(OperandKind.Close), ComparisonOperator.CrossesAbove, new Operand(OperandKind.Sma, Period: 5)),
            ]));

    public Task<IReadOnlyList<CustomStrategyDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CustomStrategyDefinition> all = _definitions.Values
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(all);
    }

    public Task<CustomStrategyDefinition?> GetAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult<CustomStrategyDefinition?>(_definitions.TryGetValue(id, out var definition) ? definition : null);

    public Task SaveAsync(CustomStrategyDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definitions[definition.Id] = definition;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_definitions.TryRemove(id, out _));
}
