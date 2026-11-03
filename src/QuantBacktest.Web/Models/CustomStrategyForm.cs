using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>The raw text of one operand: the kind is an enum name, numbers are in the invariant culture.</summary>
internal sealed record OperandInput(string? Kind, string? Period = null, string? Value = null, string? StdDevs = null);

internal sealed record ConditionInput(OperandInput Left, string? Operator, OperandInput Right);

internal sealed record RuleInput(string? Combinator, IReadOnlyList<ConditionInput> Conditions);

/// <summary>
/// What the user typed in the rule builder (P4-U9), kept as text so that a refused form is shown again exactly as
/// it was sent, added rows included. This class only checks formats (enum names, whole numbers, invariant numbers);
/// every rule of the contract (R1 to R4) is checked by ICustomStrategyFactory.Validate. Id is set in edit mode only.
/// </summary>
internal sealed partial record CustomStrategyForm(string? Id, string? Name, string? Description, RuleInput Entry, RuleInput Exit)
{
    public const string IdKey = "Id";
    public const string NameKey = "Name";
    public const string DescriptionKey = "Description";
    public const string EntryKey = "Entry";
    public const string ExitKey = "Exit";

    /// <summary>Period of the SMA of a new row, so that a new form can be saved as it is (decision 4).</summary>
    public const int DefaultPeriod = 20;

    /// <summary>A row added with "Add condition": Close > SMA(20).</summary>
    public static ConditionInput DefaultCondition { get; } = Condition(OperandKind.Close, ComparisonOperator.GreaterThan);

    /// <summary>A new strategy: buy when Close crosses above SMA(20), sell when it crosses below. Only the name is missing.</summary>
    public static CustomStrategyForm Blank() => new(
        null,
        null,
        null,
        new RuleInput(nameof(RuleCombinator.All), [Condition(OperandKind.Close, ComparisonOperator.CrossesAbove)]),
        new RuleInput(nameof(RuleCombinator.All), [Condition(OperandKind.Close, ComparisonOperator.CrossesBelow)]));

    /// <summary>Name of a form field: "Entry.Combinator", "Exit.Conditions[1].Right.Period", "Entry.Conditions[0].Operator".</summary>
    public static string FieldName(string rule, int index, string part) => $"{rule}.Conditions[{index}].{part}";

    public static string CombinatorField(string rule) => rule + ".Combinator";

    /// <summary>The form of a stored definition, numbers in their invariant form (even for an invalid definition).</summary>
    public static CustomStrategyForm FromDefinition(CustomStrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new CustomStrategyForm(definition.Id, definition.Name, definition.Description, ToInput(definition.Entry), ToInput(definition.Exit));
    }

    /// <summary>
    /// Reads a POST of the rule builder. Rows are taken in the order of their index, whatever gaps rule-builder.js
    /// left after a removal, and all of them are kept: more than MaxConditionsPerRule is reported by Validate, never cut.
    /// </summary>
    public static CustomStrategyForm Read(IFormCollection form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new CustomStrategyForm(
            Text(form, IdKey),
            Text(form, NameKey),
            Text(form, DescriptionKey),
            ReadRule(form, EntryKey),
            ReadRule(form, ExitKey));
    }

    /// <summary>
    /// Turns the text into a definition with the given id. Fields that the operand kind does not have are null (R3),
    /// whatever their text; empty fields are null too, Validate says which are missing. Name and Description are
    /// trimmed. Returns the format errors by field name; the definition is null when there is any.
    /// </summary>
    public IReadOnlyDictionary<string, string> TryBuild(string id, out CustomStrategyDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(id);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var entry = BuildRule(EntryKey, Entry, errors);
        var exit = BuildRule(ExitKey, Exit, errors);

        definition = errors.Count == 0
            ? new CustomStrategyDefinition(id, (Name ?? string.Empty).Trim(), (Description ?? string.Empty).Trim(), entry, exit)
            : null;
        return errors;
    }

    private static RuleInput ToInput(RuleSet rule) =>
        new(rule.Combinator.ToString(), rule.Conditions.Select(c => new ConditionInput(ToInput(c.Left), c.Operator.ToString(), ToInput(c.Right))).ToList());

    private static OperandInput ToInput(Operand operand) => new(
        operand.Kind.ToString(),
        operand.Period?.ToString(CultureInfo.InvariantCulture),
        operand.Value is { } value ? FormatNumber(value) : null,
        operand.StdDevs is { } stdDevs ? FormatNumber(stdDevs) : null);

    // BacktestQuery.FormatNumber goes through decimal: a stored value outside its range (or not finite, in an invalid
    // definition) is written in the round-trip notation instead of throwing.
    private static string FormatNumber(double value) =>
        double.IsFinite(value) && Math.Abs(value) < 1e27
            ? BacktestQuery.FormatNumber(value)
            : value.ToString("R", CultureInfo.InvariantCulture);

    private static ConditionInput Condition(OperandKind left, ComparisonOperator comparison) => new(
        new OperandInput(left.ToString()),
        comparison.ToString(),
        new OperandInput(nameof(OperandKind.Sma), DefaultPeriod.ToString(CultureInfo.InvariantCulture)));

    private static RuleInput ReadRule(IFormCollection form, string rule)
    {
        var prefix = rule + ".Conditions[";
        var indexes = new SortedSet<int>();
        foreach (var key in form.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && RowKey().Match(key[prefix.Length..]) is { Success: true } match
                && int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                indexes.Add(index);
            }
        }

        var conditions = indexes
            .Select(i => new ConditionInput(
                ReadOperand(form, FieldName(rule, i, "Left")),
                Text(form, FieldName(rule, i, "Operator")),
                ReadOperand(form, FieldName(rule, i, "Right"))))
            .ToList();
        return new RuleInput(Text(form, CombinatorField(rule)), conditions);
    }

    private static OperandInput ReadOperand(IFormCollection form, string prefix) => new(
        Text(form, prefix + ".Kind"),
        Text(form, prefix + ".Period"),
        Text(form, prefix + ".Value"),
        Text(form, prefix + ".StdDevs"));

    private static string? Text(IFormCollection form, string key) =>
        form.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : null;

    private static RuleSet BuildRule(string rule, RuleInput input, Dictionary<string, string> errors)
    {
        if (!RuleBuilderOptions.TryParse<RuleCombinator>(input.Combinator, out var combinator))
        {
            errors[CombinatorField(rule)] = $"{rule} rule: choose all conditions or any condition.";
        }

        var conditions = new List<RuleCondition>();
        for (var i = 0; i < input.Conditions.Count; i++)
        {
            var row = input.Conditions[i];
            var position = $"{rule} condition {i + 1}";
            var left = BuildOperand(FieldName(rule, i, "Left"), $"{position}, left operand", row.Left, errors);
            if (!RuleBuilderOptions.TryParse<ComparisonOperator>(row.Operator, out var comparison))
            {
                errors[FieldName(rule, i, "Operator")] = $"{position}: choose an operator.";
            }

            var right = BuildOperand(FieldName(rule, i, "Right"), $"{position}, right operand", row.Right, errors);
            if (left is not null && right is not null)
            {
                conditions.Add(new RuleCondition(left, comparison, right));
            }
        }

        return new RuleSet(combinator, conditions);
    }

    private static Operand? BuildOperand(string prefix, string position, OperandInput input, Dictionary<string, string> errors)
    {
        if (!RuleBuilderOptions.TryParse<OperandKind>(input.Kind, out var kind))
        {
            errors[prefix + ".Kind"] = $"{position}: choose a type.";
            return null;
        }

        int? period = null;
        double? value = null;
        double? stdDevs = null;
        var valid = true;
        if (RuleBuilderOptions.HasPeriod(kind) && !string.IsNullOrWhiteSpace(input.Period))
        {
            if (int.TryParse(input.Period.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                period = parsed;
            }
            else
            {
                errors[prefix + ".Period"] = $"{position}: the period must be a whole number.";
                valid = false;
            }
        }

        if (RuleBuilderOptions.HasValue(kind))
        {
            valid &= TryNumber(input.Value, prefix + ".Value", $"{position}: the value must be a number.", errors, out value);
        }

        if (RuleBuilderOptions.HasStdDevs(kind))
        {
            valid &= TryNumber(input.StdDevs, prefix + ".StdDevs", $"{position}: the number of standard deviations must be a number.", errors, out stdDevs);
        }

        return valid ? new Operand(kind, period, value, stdDevs) : null;
    }

    // Empty is null (Validate reports a missing value). "NaN" and "Infinity" parse: Validate refuses them (R3).
    private static bool TryNumber(string? text, string field, string message, Dictionary<string, string> errors, out double? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            number = parsed;
            return true;
        }

        errors[field] = message;
        return false;
    }

    // What follows "Entry.Conditions[": the index, then the name of one field of the row.
    [GeneratedRegex(@"^(\d{1,9})\]\.(?:Operator|(?:Left|Right)\.(?:Kind|Period|Value|StdDevs))$", RegexOptions.CultureInvariant)]
    private static partial Regex RowKey();
}
