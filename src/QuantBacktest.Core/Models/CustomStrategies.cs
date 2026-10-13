namespace QuantBacktest.Core.Models;

/// <summary>
/// A value that a custom-strategy condition can compare. Exact definitions: docs/CONTRACT.md section 8.
/// </summary>
public enum OperandKind
{
    Constant = 0,
    Close = 1,
    Open = 2,
    High = 3,
    Low = 4,
    Volume = 5,
    Sma = 6,
    Ema = 7,
    Rsi = 8,
    RateOfChange = 9,
    BollingerUpper = 10,
    BollingerMiddle = 11,
    BollingerLower = 12,
    MacdLine = 13,
    MacdSignal = 14,
    HighestHigh = 15,
    LowestLow = 16,
}

/// <summary>
/// Period is required for Sma, Ema, Rsi, RateOfChange, Bollinger*, HighestHigh, LowestLow and forbidden otherwise.
/// Value is required for Constant and forbidden otherwise. StdDevs is optional for Bollinger* (default 2) and forbidden otherwise.
/// </summary>
public sealed record Operand(
    OperandKind Kind,
    int? Period = null,
    double? Value = null,
    double? StdDevs = null);

public enum ComparisonOperator
{
    GreaterThan = 0,
    LessThan = 1,
    CrossesAbove = 2,
    CrossesBelow = 3,
}

public sealed record RuleCondition(Operand Left, ComparisonOperator Operator, Operand Right);

public enum RuleCombinator
{
    /// <summary>All conditions must be true (AND).</summary>
    All = 0,

    /// <summary>At least one condition must be true (OR).</summary>
    Any = 1,
}

public sealed record RuleSet(RuleCombinator Combinator, IReadOnlyList<RuleCondition> Conditions);

/// <summary>
/// A strategy defined without code in the UI. Id must equal Conventions.CreateCustomStrategyId(Name).
/// Entry true at a bar gives Buy, Exit true gives Sell, both true gives Sell.
/// </summary>
public sealed record CustomStrategyDefinition(
    string Id,
    string Name,
    string Description,
    RuleSet Entry,
    RuleSet Exit);
