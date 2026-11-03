using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>One choice of a select of the rule builder: the enum name is the submitted value.</summary>
internal sealed record RuleBuilderOption(string Value, string Label);

/// <summary>
/// Choices of the rule builder (P4-U9), labelled with the notation of docs/CONTRACT.md section 8, and which operand
/// field belongs to which type (R3). Only which field to show: the ranges and every other rule stay in
/// ICustomStrategyFactory.Validate.
/// </summary>
internal static class RuleBuilderOptions
{
    public static IReadOnlyList<RuleBuilderOption> Kinds { get; } =
    [
        Option(OperandKind.Close, "Close"),
        Option(OperandKind.Open, "Open"),
        Option(OperandKind.High, "High"),
        Option(OperandKind.Low, "Low"),
        Option(OperandKind.Volume, "Volume"),
        Option(OperandKind.Sma, "SMA"),
        Option(OperandKind.Ema, "EMA"),
        Option(OperandKind.Rsi, "RSI"),
        Option(OperandKind.RateOfChange, "ROC"),
        Option(OperandKind.BollingerUpper, "BB upper"),
        Option(OperandKind.BollingerMiddle, "BB middle"),
        Option(OperandKind.BollingerLower, "BB lower"),
        Option(OperandKind.MacdLine, "MACD line"),
        Option(OperandKind.MacdSignal, "MACD signal"),
        Option(OperandKind.HighestHigh, "Highest high"),
        Option(OperandKind.LowestLow, "Lowest low"),
        Option(OperandKind.Constant, "Constant"),
    ];

    public static IReadOnlyList<RuleBuilderOption> Operators { get; } =
    [
        Option(ComparisonOperator.GreaterThan, ">"),
        Option(ComparisonOperator.LessThan, "<"),
        Option(ComparisonOperator.CrossesAbove, "crosses above"),
        Option(ComparisonOperator.CrossesBelow, "crosses below"),
    ];

    public static IReadOnlyList<RuleBuilderOption> Combinators { get; } =
    [
        Option(RuleCombinator.All, "all conditions"),
        Option(RuleCombinator.Any, "any condition"),
    ];

    /// <summary>R3: Period for the indicators with a window.</summary>
    public static bool HasPeriod(OperandKind kind) => kind is OperandKind.Sma or OperandKind.Ema or OperandKind.Rsi
        or OperandKind.RateOfChange or OperandKind.HighestHigh or OperandKind.LowestLow || IsBollinger(kind);

    /// <summary>R3: Value for Constant only.</summary>
    public static bool HasValue(OperandKind kind) => kind == OperandKind.Constant;

    /// <summary>R3: StdDevs for the Bollinger bands only.</summary>
    public static bool HasStdDevs(OperandKind kind) => IsBollinger(kind);

    /// <summary>Space-separated enum names, rendered in data-kinds for rule-builder.js ("Sma Ema ...").</summary>
    public static string KindsWhere(Func<OperandKind, bool> hasField)
    {
        ArgumentNullException.ThrowIfNull(hasField);
        return string.Join(' ', Enum.GetValues<OperandKind>().Where(hasField));
    }

    /// <summary>
    /// The enum value whose name is exactly <paramref name="text"/>. Numbers ("5", "99") are refused: Enum.TryParse
    /// would accept them, even undefined ones.
    /// </summary>
    public static bool TryParse<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsBollinger(OperandKind kind) =>
        kind is OperandKind.BollingerUpper or OperandKind.BollingerMiddle or OperandKind.BollingerLower;

    private static RuleBuilderOption Option<TEnum>(TEnum value, string label)
        where TEnum : struct, Enum => new(value.ToString(), label);
}
