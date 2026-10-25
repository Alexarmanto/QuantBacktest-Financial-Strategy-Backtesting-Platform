using System.Text.Json;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One element of GET /api/strategies (PART-4 section 5), serialized in camelCase. Built only from IStrategy,
/// so any strategy of the catalog appears without a change here. Enum values are spelled out explicitly:
/// timeframes as in the URL ("daily"), type "integer" | "decimal", category in camelCase ("trendFollowing").
/// </summary>
internal sealed record StrategyApiModel(
    string Id,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> SupportedTimeframes,
    IReadOnlyList<StrategyParameterApiModel> Parameters,
    StrategyDocumentationApiModel Documentation)
{
    public static StrategyApiModel From(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return new StrategyApiModel(
            strategy.Id,
            strategy.Name,
            strategy.Description,
            JsonNamingPolicy.CamelCase.ConvertName(strategy.Category.ToString()),
            strategy.SupportedTimeframes.Select(BacktestQuery.FormatTimeframe).ToList(),
            strategy.Parameters.Select(StrategyParameterApiModel.From).ToList(),
            new StrategyDocumentationApiModel(
                strategy.Documentation.Idea,
                strategy.Documentation.EntryRule,
                strategy.Documentation.ExitRule,
                strategy.Documentation.Risks));
    }
}

internal sealed record StrategyParameterApiModel(
    string Key,
    string Label,
    string Type,
    double Default,
    double Min,
    double Max,
    double Step,
    string? Description)
{
    public static StrategyParameterApiModel From(ParameterDefinition definition) => new(
        definition.Key,
        definition.Label,
        definition.Type switch
        {
            ParameterType.Integer => "integer",
            ParameterType.Decimal => "decimal",
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Type, "Unknown parameter type."),
        },
        definition.DefaultValue,
        definition.Min,
        definition.Max,
        definition.Step,
        definition.Description);
}

internal sealed record StrategyDocumentationApiModel(string Idea, string EntryRule, string ExitRule, string Risks);
