using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>Catalog returning exactly the given strategies, in the given order.</summary>
internal sealed class StubStrategyCatalog(params IStrategy[] strategies) : IStrategyCatalog
{
    public Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IStrategy>>(strategies);

    public Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(strategies.FirstOrDefault(s => s.Id == id));
}

/// <summary>Descriptive data only: the web never runs a strategy itself.</summary>
internal sealed class StubStrategy : IStrategy
{
    public required string Id { get; init; }

    public string Name { get; init; } = "Stub strategy";

    public string Description { get; init; } = "Stub description.";

    public StrategyCategory Category { get; init; } = StrategyCategory.Baseline;

    public StrategyDocumentation Documentation { get; init; } = new("Idea.", "Entry.", "Exit.", "Risks.");

    public IReadOnlyList<Timeframe> SupportedTimeframes { get; init; } = [Timeframe.Daily, Timeframe.Weekly];

    public IReadOnlyList<ParameterDefinition> Parameters { get; init; } = [];

    public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters) =>
        throw new NotSupportedException();

    public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters) =>
        throw new NotSupportedException();
}
