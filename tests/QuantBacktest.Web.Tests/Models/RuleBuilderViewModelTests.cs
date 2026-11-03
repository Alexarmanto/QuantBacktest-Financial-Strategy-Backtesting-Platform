using QuantBacktest.Core;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class RuleBuilderViewModelTests
{
    private static RuleBuilderViewModel Edit(string pathBase = "") =>
        RuleBuilderViewModel.ForEdit(FakeCustomStrategyStore.Example, [], pathBase);

    [Fact] // P4-U9: field names bound by CustomStrategyForm.Read, element ids, the template with its placeholder
    public void Rows_HaveTheFieldNamesOfTheForm()
    {
        var entry = Edit().Entry;

        entry.CombinatorName.Should().Be("Entry.Combinator");
        entry.Rows.Select(r => r.Number).Should().Equal(1, 2);
        entry.Rows[1].Name("Right.Value").Should().Be("Entry.Conditions[1].Right.Value");
        entry.Rows[1].ElementId("Right.Value").Should().Be("entry-1-right-value");
        entry.Template.Name("Left.Kind").Should().Be("Entry.Conditions[__index__].Left.Kind");
        entry.Template.ElementId("Operator").Should().Be("entry-__index__-operator");
        entry.Template.Input.Should().Be(CustomStrategyForm.DefaultCondition);
        Edit().Exit.Rows.Single().Name("Operator").Should().Be("Exit.Conditions[0].Operator");
    }

    [Fact] // P4-U9: a format error is shown under its row and field
    public void Rows_ShowTheirFormatErrors()
    {
        var errors = new Dictionary<string, string> { ["Exit.Conditions[0].Left.Period"] = "Bad period." };

        var model = RuleBuilderViewModel.Refused(false, CustomStrategyForm.Blank(), [], errors, "");

        model.Exit.Rows.Single().Error("Left.Period").Should().Be("Bad period.");
        model.Exit.Rows.Single().Error("Right.Period").Should().BeNull();
        model.Entry.Template.Error("Left.Period").Should().BeNull();
        model.HasFieldErrors.Should().BeTrue();
        model.ProblemsHeading.Should().BeNull();
    }

    [Fact] // P4-U9, docs/CONTRACT.md section 8: labels in the notation of the contract, values = enum names
    public void Options_UseTheNotationOfTheContract()
    {
        RuleBuilderOptions.Kinds.Select(o => o.Label).Should().Equal(
            "Close", "Open", "High", "Low", "Volume", "SMA", "EMA", "RSI", "ROC", "BB upper", "BB middle", "BB lower",
            "MACD line", "MACD signal", "Highest high", "Lowest low", "Constant");
        RuleBuilderOptions.Kinds.Select(o => o.Value).Should().BeEquivalentTo(Enum.GetNames<OperandKind>());
        RuleBuilderOptions.Operators.Should().Equal(
            new RuleBuilderOption("GreaterThan", ">"),
            new RuleBuilderOption("LessThan", "<"),
            new RuleBuilderOption("CrossesAbove", "crosses above"),
            new RuleBuilderOption("CrossesBelow", "crosses below"));
        RuleBuilderOptions.Combinators.Should().Equal(
            new RuleBuilderOption("All", "all conditions"),
            new RuleBuilderOption("Any", "any condition"));
    }

    [Fact] // P4-U9, R3: which operand field each kind shows (data-kinds of rule-builder.js)
    public void Fields_FollowR3()
    {
        RuleBuilderOptions.KindsWhere(RuleBuilderOptions.HasPeriod).Should().Be(
            "Sma Ema Rsi RateOfChange BollingerUpper BollingerMiddle BollingerLower HighestHigh LowestLow");
        RuleBuilderOptions.KindsWhere(RuleBuilderOptions.HasValue).Should().Be("Constant");
        RuleBuilderOptions.KindsWhere(RuleBuilderOptions.HasStdDevs).Should().Be("BollingerUpper BollingerMiddle BollingerLower");
    }

    [Fact] // P4-U9, decision 5: New has no Delete; links follow Request.PathBase
    public void New_Links()
    {
        var model = RuleBuilderViewModel.ForNew("/app");

        model.Title.Should().Be("New custom strategy");
        model.SaveUrl.Should().Be("/app/Strategies/Custom/Save");
        model.CancelUrl.Should().Be("/app/Strategies#custom");
        model.DeleteUrl.Should().BeNull();
        model.DeleteConfirmation.Should().BeNull();
        model.MaxConditions.Should().Be(Conventions.MaxConditionsPerRule);
    }

    [Fact] // P4-U9, decision 5: Edit offers the Delete POST of the catalog, with the same confirmation
    public void Edit_Links()
    {
        var model = Edit("/app");

        model.Title.Should().Be("Edit custom strategy");
        model.SaveUrl.Should().Be("/app/Strategies/Custom/Save");
        model.CancelUrl.Should().Be("/app/Strategies#strategy-custom-trend-dip");
        model.DeleteUrl.Should().Be("/app/Strategies/Custom/custom-trend-dip/Delete");
        model.DeleteConfirmation.Should().Be("Delete the strategy \"Trend dip\"? This cannot be undone.");
        model.ProblemsHeading.Should().BeNull();
    }

    [Fact] // P4-U9, R2: the counter is right before any script runs; bounds = data-min / data-max of rule-builder.js
    public void Counter_InitialText()
    {
        var blank = RuleBuilderViewModel.ForNew("");
        var trendDip = Edit();

        FakeCustomStrategyStore.Example.Id.Should().Be("custom-trend-dip");
        blank.Entry.CountText.Should().Be("1 of 5 conditions (minimum 1)");
        blank.Exit.CountText.Should().Be("1 of 5 conditions (minimum 1)");
        trendDip.Entry.CountText.Should().Be("2 of 5 conditions (minimum 1)");
        trendDip.Exit.CountText.Should().Be("1 of 5 conditions (minimum 1)");
        trendDip.Entry.ConditionCount.Should().Be(2);
        trendDip.Entry.CountSuffix.Should().Be(" of 5 conditions (minimum 1)");
        trendDip.Entry.MinConditions.Should().Be(RuleBuilderViewModel.MinConditions).And.Be(1);
        trendDip.Entry.MaxConditions.Should().Be(Conventions.MaxConditionsPerRule);
    }

    [Fact] // P4-U9: a stored definition that fails Validate opens with its problems under a dedicated heading
    public void Edit_InvalidStoredDefinition_ShowsItsProblems()
    {
        var model = RuleBuilderViewModel.ForEdit(FakeCustomStrategyStore.Example, ["Broken."], "");

        model.ProblemsHeading.Should().Be(RuleBuilderViewModel.StoredInvalidHeading);
        model.Problems.Should().Equal("Broken.");
    }
}
