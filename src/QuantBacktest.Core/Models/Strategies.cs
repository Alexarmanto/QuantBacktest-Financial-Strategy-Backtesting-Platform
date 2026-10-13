namespace QuantBacktest.Core.Models;

/// <summary>
/// Decision emitted by a strategy at the close of a bar.
/// Buy while already long, and Sell while flat, are ignored by the engine.
/// </summary>
public enum Signal
{
    Hold = 0,
    Buy = 1,
    Sell = -1,
}

public enum ParameterType
{
    Integer = 0,
    Decimal = 1,
}

public enum StrategyCategory
{
    TrendFollowing = 0,
    MeanReversion = 1,
    Momentum = 2,
    Breakout = 3,
    Calendar = 4,
    Baseline = 5,
    Custom = 6,
}

/// <summary>
/// Describes one tunable parameter of a strategy. The web UI builds its form from these definitions,
/// and the backtest service validates incoming values against Min and Max.
/// </summary>
public sealed record ParameterDefinition(
    string Key,
    string Label,
    ParameterType Type,
    double DefaultValue,
    double Min,
    double Max,
    double Step,
    string? Description = null);

/// <summary>Explanatory card of a strategy, shown in the UI and the manual. All texts in English, plain text.</summary>
public sealed record StrategyDocumentation(
    string Idea,
    string EntryRule,
    string ExitRule,
    string Risks);

/// <summary>Where an indicator is drawn: on top of the price chart, or in its own chart below it.</summary>
public enum IndicatorPane
{
    Price = 0,
    Oscillator = 1,
}

/// <summary>
/// One line to draw on a chart (for example "SMA 20", "RSI 14"). Values has exactly one entry per bar
/// of the series it was computed on; null during warm-up. ReferenceLines are horizontal guides (RSI 30 and 70).
/// </summary>
public sealed record IndicatorSeries(
    string Key,
    string Label,
    IndicatorPane Pane,
    IReadOnlyList<double?> Values,
    IReadOnlyList<double> ReferenceLines);
