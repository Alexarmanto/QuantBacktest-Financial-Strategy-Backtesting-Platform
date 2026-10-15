namespace QuantBacktest.Data.Storage.Entities;

/// <summary>Table CustomStrategies, key Id: one serialised CustomStrategyDefinition (P1-D8, P1-D14).</summary>
internal sealed class CustomStrategyEntity
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Json { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
