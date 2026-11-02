using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>One row of the parameter table of a card. Numbers in their invariant URL form, as in the backtest form.</summary>
internal sealed record ParameterRow(
    string Label,
    string Type,
    string Default,
    string Min,
    string Max,
    string Step,
    string? Description);

/// <summary>
/// One card of the strategy catalog (P4-U8), built from IStrategy only. EditUrl and DeleteUrl exist for custom
/// strategies only. Edit is rendered disabled until the rule builder PR enables it.
/// </summary>
internal sealed record StrategyCardViewModel(
    string Id,
    string Name,
    string CategoryLabel,
    string Description,
    StrategyDocumentation Documentation,
    IReadOnlyList<string> Timeframes,
    IReadOnlyList<ParameterRow> Parameters,
    string UseUrl,
    string? EditUrl,
    string? DeleteUrl)
{
    public const string NoParameters = "This strategy has no parameters.";

    public const string CustomPathPrefix = "/Strategies/Custom/";

    public bool IsCustom => DeleteUrl is not null;

    /// <summary>Anchor of the card ("strategy-sma-crossover"), linked from the user manual.</summary>
    public string Anchor => "strategy-" + Id;

    public string DeleteConfirmation => $"Delete the strategy \"{Name}\"? This cannot be undone.";

    /// <param name="pathBase">Request.PathBase of the current request ("" when the app is served at the root).</param>
    public static StrategyCardViewModel Create(IStrategy strategy, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(pathBase);

        var customPath = pathBase + CustomPathPrefix + Uri.EscapeDataString(strategy.Id);
        var isCustom = StrategyGroups.IsCustom(strategy);
        return new StrategyCardViewModel(
            strategy.Id,
            strategy.Name,
            CategoryText(strategy.Category),
            strategy.Description,
            strategy.Documentation,
            strategy.SupportedTimeframes.Order().Select(TimeframeText).ToList(),
            strategy.Parameters.Select(Row).ToList(),
            pathBase + ResultActionsViewModel.EditSettingsPath + "?" + BacktestQuery.FormQueryString(strategy),
            isCustom ? customPath + "/Edit" : null,
            isCustom ? customPath + "/Delete" : null);
    }

    /// <summary>Readable label of a category: "TrendFollowing" → "Trend following".</summary>
    public static string CategoryText(StrategyCategory category) => category switch
    {
        StrategyCategory.TrendFollowing => "Trend following",
        StrategyCategory.MeanReversion => "Mean reversion",
        StrategyCategory.Momentum => "Momentum",
        StrategyCategory.Breakout => "Breakout",
        StrategyCategory.Calendar => "Calendar effect",
        StrategyCategory.Baseline => "Baseline",
        StrategyCategory.Custom => "Custom (no-code)",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown strategy category."),
    };

    private static string TimeframeText(Timeframe timeframe) => timeframe switch
    {
        Timeframe.Daily => "Daily",
        Timeframe.Weekly => "Weekly",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };

    private static ParameterRow Row(ParameterDefinition definition) => new(
        definition.Label,
        definition.Type switch
        {
            ParameterType.Integer => "Integer",
            ParameterType.Decimal => "Decimal",
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.Type, "Unknown parameter type."),
        },
        BacktestQuery.FormatNumber(definition.DefaultValue),
        BacktestQuery.FormatNumber(definition.Min),
        BacktestQuery.FormatNumber(definition.Max),
        BacktestQuery.FormatNumber(definition.Step),
        definition.Description);
}
