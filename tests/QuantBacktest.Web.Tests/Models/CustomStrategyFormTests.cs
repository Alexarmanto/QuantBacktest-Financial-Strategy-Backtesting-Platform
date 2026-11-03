using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class CustomStrategyFormTests
{
    private static readonly ConditionInput CloseAboveSma = new(new OperandInput("Close"), "GreaterThan", new OperandInput("Sma", "20"));

    private static CustomStrategyForm FormWith(params ConditionInput[] entry) =>
        new(null, "Name", null, new RuleInput("All", entry), new RuleInput("Any", [CloseAboveSma]));

    private static CustomStrategyDefinition Build(CustomStrategyForm form)
    {
        var errors = form.TryBuild("custom-name", out var definition);
        errors.Should().BeEmpty();
        return definition!;
    }

    private static void InFrench(Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact] // P4-U9, R3: only the fields of the operand kind are kept, whatever was typed in the hidden ones
    public void TryBuild_KeepsOnlyTheFieldsOfTheKind()
    {
        var definition = Build(FormWith(
            new ConditionInput(new OperandInput("Close", "14", "3", "2"), "CrossesAbove", new OperandInput("Constant", "5", "0.5", "2")),
            new ConditionInput(new OperandInput("BollingerLower", "20", "7", ""), "LessThan", new OperandInput("BollingerUpper", "10", null, "2.5")),
            new ConditionInput(new OperandInput("MacdLine", "12"), "CrossesBelow", new OperandInput("MacdSignal", null, null, "2"))));

        definition.Entry.Conditions.Should().Equal(
            new RuleCondition(new Operand(OperandKind.Close), ComparisonOperator.CrossesAbove, new Operand(OperandKind.Constant, Value: 0.5)),
            new RuleCondition(new Operand(OperandKind.BollingerLower, Period: 20), ComparisonOperator.LessThan, new Operand(OperandKind.BollingerUpper, Period: 10, StdDevs: 2.5)),
            new RuleCondition(new Operand(OperandKind.MacdLine), ComparisonOperator.CrossesBelow, new Operand(OperandKind.MacdSignal)));
        definition.Entry.Combinator.Should().Be(RuleCombinator.All);
        definition.Exit.Combinator.Should().Be(RuleCombinator.Any);
    }

    [Fact] // P4-U9: empty fields are null, Validate says which are missing (R3); not checked here
    public void TryBuild_EmptyFields_AreNull()
    {
        var definition = Build(FormWith(new ConditionInput(new OperandInput("Sma", " "), "GreaterThan", new OperandInput("Constant", null, ""))));

        definition.Entry.Conditions.Single().Should().Be(
            new RuleCondition(new Operand(OperandKind.Sma), ComparisonOperator.GreaterThan, new Operand(OperandKind.Constant)));
    }

    [Fact] // P4-U9, R1: id as given; name and description trimmed; null description is empty
    public void TryBuild_UsesTheId_TrimsTheTexts()
    {
        var form = FormWith(CloseAboveSma) with { Name = "  My name ", Description = null };

        var definition = Build(form);

        definition.Id.Should().Be("custom-name");
        definition.Name.Should().Be("My name");
        definition.Description.Should().BeEmpty();
    }

    [Fact] // P4-U9, CLAUDE.md: numbers are read in the invariant culture, whatever the culture of the thread
    public void TryBuild_ReadsInvariantNumbers()
    {
        InFrench(() =>
        {
            var form = FormWith(new ConditionInput(new OperandInput("Rsi", "2"), "LessThan", new OperandInput("Constant", Value: "0.5")));
            Build(form).Entry.Conditions.Single().Right.Value.Should().Be(0.5);

            var french = FormWith(new ConditionInput(new OperandInput("Rsi", "2"), "LessThan", new OperandInput("Constant", Value: "0,5")));
            french.TryBuild("custom-name", out var definition)
                .Should().ContainKey("Entry.Conditions[0].Right.Value")
                .WhoseValue.Should().Be("Entry condition 1, right operand: the value must be a number.");
            definition.Should().BeNull();
        });
    }

    [Theory] // P4-U9: a period is a whole number
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("1e3")]
    public void TryBuild_PeriodNotWhole_IsAFormatError(string period)
    {
        var errors = FormWith(new ConditionInput(new OperandInput("Ema", period), "GreaterThan", new OperandInput("Close"))).TryBuild("custom-name", out _);

        errors.Should().Equal(new Dictionary<string, string>
        {
            ["Entry.Conditions[0].Left.Period"] = "Entry condition 1, left operand: the period must be a whole number.",
        });
    }

    [Fact] // P4-U9, R3: "NaN" is a number for the web; Validate refuses it as not finite
    public void TryBuild_NaN_IsLeftToValidate()
    {
        var definition = Build(FormWith(new ConditionInput(new OperandInput("Close"), "GreaterThan", new OperandInput("Constant", Value: "NaN"))));

        definition.Entry.Conditions.Single().Right.Value.Should().Be(double.NaN);
    }

    [Theory] // P4-U9: enum values by name only; numbers, even defined ones, and unknown names are refused
    [InlineData("5", "GreaterThan", "All", "Entry.Conditions[0].Left.Kind")]
    [InlineData("Close", "99", "All", "Entry.Conditions[0].Operator")]
    [InlineData("Close", "greaterthan", "All", "Entry.Conditions[0].Operator")]
    [InlineData("Close", "GreaterThan", "1", "Entry.Combinator")]
    [InlineData(null, "GreaterThan", "All", "Entry.Conditions[0].Left.Kind")]
    public void TryBuild_EnumNotByName_IsAFormatError(string? kind, string comparison, string combinator, string field)
    {
        var form = new CustomStrategyForm(
            null, "Name", null,
            new RuleInput(combinator, [new ConditionInput(new OperandInput(kind), comparison, new OperandInput("Sma", "20"))]),
            new RuleInput("Any", [CloseAboveSma]));

        var errors = form.TryBuild("custom-name", out var definition);

        errors.Keys.Should().Equal(field);
        definition.Should().BeNull();
    }

    [Fact] // P4-U9: rows in the order of their index, gaps ignored, all kept (no cut at MaxConditionsPerRule)
    public void Read_TakesRowsInIndexOrder_AndKeepsThemAll()
    {
        var fields = new Dictionary<string, StringValues>
        {
            ["Id"] = "custom-x",
            ["Name"] = "X",
            ["Entry.Combinator"] = "Any",
            ["Exit.Combinator"] = "All",
            ["Unrelated[0].Left.Kind"] = "Close",
            ["Entry.Conditions[x].Left.Kind"] = "Close",
        };
        foreach (var index in new[] { 12, 0, 3, 7, 5, 9 })
        {
            fields[$"Entry.Conditions[{index}].Left.Kind"] = "Sma";
            fields[$"Entry.Conditions[{index}].Left.Period"] = index.ToString(CultureInfo.InvariantCulture);
        }

        fields["Exit.Conditions[2].Operator"] = "LessThan";

        var form = CustomStrategyForm.Read(new FormCollection(fields));

        form.Id.Should().Be("custom-x");
        form.Entry.Combinator.Should().Be("Any");
        form.Entry.Conditions.Select(c => c.Left.Period).Should().Equal("0", "3", "5", "7", "9", "12");
        form.Exit.Conditions.Should().Equal(new ConditionInput(new OperandInput(null), "LessThan", new OperandInput(null)));
    }

    [Fact] // P4-U9: indexes are sorted as numbers, not as text: [2] comes before [10]
    public void Read_SortsIndexesNumerically()
    {
        var form = CustomStrategyForm.Read(new FormCollection(new Dictionary<string, StringValues>
        {
            ["Entry.Conditions[10].Left.Kind"] = "Ema",
            ["Entry.Conditions[2].Left.Kind"] = "Sma",
        }));

        form.Entry.Conditions.Select(c => c.Left.Kind).Should().Equal("Sma", "Ema");
    }

    [Fact] // P4-U9: a stored definition gives back the same definition (prefill of Edit)
    public void FromDefinition_RoundTrips()
    {
        var example = FakeCustomStrategyStore.Example;

        var form = CustomStrategyForm.FromDefinition(example);
        form.TryBuild(example.Id, out var definition).Should().BeEmpty();

        definition.Should().BeEquivalentTo(example);
        form.Id.Should().Be(example.Id);
    }

    [Fact] // P4-U9, CLAUDE.md: numbers shown in their invariant form (BacktestQuery.FormatNumber), even in French
    public void FromDefinition_WritesInvariantNumbers()
    {
        var definition = FakeCustomStrategyStore.Example with
        {
            Entry = new RuleSet(RuleCombinator.All,
            [
                new RuleCondition(new Operand(OperandKind.BollingerLower, 200, StdDevs: 2.5), ComparisonOperator.GreaterThan, new Operand(OperandKind.Constant, Value: 0.00001)),
                new RuleCondition(new Operand(OperandKind.Constant, Value: 1e30), ComparisonOperator.LessThan, new Operand(OperandKind.Constant, Value: double.NaN)),
            ]),
        };

        InFrench(() =>
        {
            var rows = CustomStrategyForm.FromDefinition(definition).Entry.Conditions;

            rows[0].Should().Be(new ConditionInput(new OperandInput("BollingerLower", "200", null, "2.5"), "GreaterThan", new OperandInput("Constant", null, "0.00001")));
            rows[1].Left.Value.Should().Be("1E+30");
            rows[1].Right.Value.Should().Be("NaN");
        });
    }

    [Fact] // P4-U9, decision 4: the blank form holds a complete strategy (Close crosses SMA(20)), only the name is empty
    public void Blank_HasDefaultRules()
    {
        var blank = CustomStrategyForm.Blank();

        blank.Id.Should().BeNull();
        blank.Name.Should().BeNull();
        blank.Entry.Conditions.Should().Equal(new ConditionInput(new OperandInput("Close"), "CrossesAbove", new OperandInput("Sma", "20")));
        blank.Exit.Conditions.Should().Equal(new ConditionInput(new OperandInput("Close"), "CrossesBelow", new OperandInput("Sma", "20")));
        CustomStrategyForm.DefaultCondition.Should().Be(CloseAboveSma);
    }
}
