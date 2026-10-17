using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies.Tests;

// Checks of the no-code rules, R1 to R4 (P2-N1). One test per rule, with the expected message.
public class RuleValidationTests
{
    private readonly RuleStrategyFactory _factory = new RuleStrategyFactory();

    [Fact]
    public void ValidDefinition_HasNoProblem_AndGivesACustomStrategy() // P2-N1, R8
    {
        CustomStrategyDefinition definition = Definition("My test", "custom-my-test", Rule(Close(), ComparisonOperator.GreaterThan, Sma(20)));

        _factory.Validate(definition).Should().BeEmpty();
        IStrategy strategy = _factory.Create(definition);

        strategy.Id.Should().Be("custom-my-test");
        strategy.Name.Should().Be("My test");
        strategy.Category.Should().Be(StrategyCategory.Custom);
        strategy.Parameters.Should().BeEmpty();
        strategy.SupportedTimeframes.Should().Equal(Timeframe.Daily, Timeframe.Weekly);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("A name that is much too long for the strategy form of the website")]
    public void Name_EmptyOrLongerThan60_IsRefused(string name) // R1
    {
        CustomStrategyDefinition definition = Definition(name, "custom-x", Rule(Close(), ComparisonOperator.GreaterThan, Sma(20)));

        _factory.Validate(definition).Should().Equal("The name must contain 1 to 60 characters.");
    }

    [Fact]
    public void Name_WithoutLetterOrDigit_IsRefused() // R1
    {
        CustomStrategyDefinition definition = Definition("!!!", "custom-x", Rule(Close(), ComparisonOperator.GreaterThan, Sma(20)));

        _factory.Validate(definition).Should().Equal("The name must contain at least one letter or digit.");
    }

    [Fact]
    public void Id_NotMadeFromTheName_IsRefused() // R1
    {
        CustomStrategyDefinition definition = Definition("My test", "custom-other", Rule(Close(), ComparisonOperator.GreaterThan, Sma(20)));

        _factory.Validate(definition).Should().Equal("The id must be 'custom-my-test'.");
    }

    [Fact]
    public void Description_LongerThan500_IsRefused() // R1
    {
        RuleSet rule = Rule(Close(), ComparisonOperator.GreaterThan, Sma(20));
        CustomStrategyDefinition definition = new CustomStrategyDefinition("custom-my-test", "My test", new string('x', 501), rule, rule);

        _factory.Validate(definition).Should().Equal("The description must contain at most 500 characters.");
    }

    [Fact]
    public void Rule_WithoutCondition_OrWithMoreThan5_IsRefused() // R2
    {
        RuleSet empty = new RuleSet(RuleCombinator.All, new RuleCondition[0]);
        RuleCondition condition = new RuleCondition(Close(), ComparisonOperator.GreaterThan, Sma(20));
        RuleSet six = new RuleSet(RuleCombinator.Any, new RuleCondition[] { condition, condition, condition, condition, condition, condition });
        CustomStrategyDefinition definition = new CustomStrategyDefinition("custom-my-test", "My test", "", empty, six);

        _factory.Validate(definition).Should().Equal(
            "The entry rule needs 1 to 5 conditions.",
            "The exit rule needs 1 to 5 conditions.");
    }

    [Fact]
    public void Period_MissingOrOutOfRange_IsRefusedWithItsPosition() // R3
    {
        // the example of the notice: second entry condition, right operand
        RuleSet entry = new RuleSet(RuleCombinator.All, new RuleCondition[]
        {
            new RuleCondition(Close(), ComparisonOperator.GreaterThan, Sma(20)),
            new RuleCondition(Close(), ComparisonOperator.GreaterThan, new Operand(OperandKind.Sma)),
        });
        RuleSet exit = new RuleSet(RuleCombinator.Any, new RuleCondition[]
        {
            new RuleCondition(new Operand(OperandKind.Rsi, Period: 1), ComparisonOperator.LessThan, Constant(30)),
            new RuleCondition(new Operand(OperandKind.Ema, Period: 501), ComparisonOperator.LessThan, Close()),
        });
        CustomStrategyDefinition definition = new CustomStrategyDefinition("custom-my-test", "My test", "", entry, exit);

        _factory.Validate(definition).Should().Equal(
            "Entry condition 2: the right operand needs a period between 1 and 500.",
            "Exit condition 1: the left operand needs a period between 2 and 500.",
            "Exit condition 2: the left operand needs a period between 1 and 500.");
    }

    [Fact]
    public void ForbiddenFields_AreRefused() // R3
    {
        RuleSet entry = new RuleSet(RuleCombinator.All, new RuleCondition[]
        {
            new RuleCondition(new Operand(OperandKind.Close, Period: 5), ComparisonOperator.GreaterThan, new Operand(OperandKind.Sma, Period: 20, Value: 3)),
            new RuleCondition(new Operand(OperandKind.Rsi, Period: 14, StdDevs: 2), ComparisonOperator.LessThan, Constant(30)),
        });
        RuleSet exit = Rule(Close(), ComparisonOperator.GreaterThan, Sma(20));
        CustomStrategyDefinition definition = new CustomStrategyDefinition("custom-my-test", "My test", "", entry, exit);

        _factory.Validate(definition).Should().Equal(
            "Entry condition 1: the left operand cannot have a period.",
            "Entry condition 1: the right operand cannot have a value.",
            "Entry condition 2: the left operand cannot have a number of standard deviations.");
    }

    [Fact]
    public void ConstantWithoutFiniteValue_AndWrongStdDevs_AreRefused() // R3
    {
        RuleSet entry = new RuleSet(RuleCombinator.All, new RuleCondition[]
        {
            new RuleCondition(Close(), ComparisonOperator.GreaterThan, new Operand(OperandKind.Constant)),
            new RuleCondition(Close(), ComparisonOperator.GreaterThan, new Operand(OperandKind.Constant, Value: double.PositiveInfinity)),
            new RuleCondition(Close(), ComparisonOperator.LessThan, new Operand(OperandKind.BollingerLower, Period: 20, StdDevs: 6)),
        });
        RuleSet exit = Rule(Close(), ComparisonOperator.GreaterThan, Sma(20));
        CustomStrategyDefinition definition = new CustomStrategyDefinition("custom-my-test", "My test", "", entry, exit);

        _factory.Validate(definition).Should().Equal(
            "Entry condition 1: the right operand needs a finite value.",
            "Entry condition 2: the right operand needs a finite value.",
            "Entry condition 3: the right operand needs a number of standard deviations between 0.5 and 5.");
    }

    [Fact]
    public void TwoConstants_AreRefused() // R4
    {
        RuleSet entry = Rule(Close(), ComparisonOperator.GreaterThan, Sma(20));
        RuleSet exit = Rule(Constant(10), ComparisonOperator.GreaterThan, Constant(30));
        CustomStrategyDefinition definition = new CustomStrategyDefinition("custom-my-test", "My test", "", entry, exit);

        _factory.Validate(definition).Should().Equal("Exit condition 1: cannot compare two constants.");
    }

    [Fact]
    public void Create_InvalidDefinition_ThrowsWithAllTheProblems() // P2-N1
    {
        RuleSet entry = Rule(Close(), ComparisonOperator.GreaterThan, new Operand(OperandKind.Sma));
        RuleSet exit = Rule(Constant(10), ComparisonOperator.GreaterThan, Constant(30));
        CustomStrategyDefinition definition = new CustomStrategyDefinition("custom-other", "My test", "", entry, exit);

        string message = "";
        try
        {
            _factory.Create(definition);
        }
        catch (BacktestValidationException exception)
        {
            message = exception.Message;
        }

        message.Should().Be(
            "The id must be 'custom-my-test'. "
            + "Entry condition 1: the right operand needs a period between 1 and 500. "
            + "Exit condition 1: cannot compare two constants.");
    }

    // ---- helpers to write the rules quickly ----

    private static Operand Close()
    {
        return new Operand(OperandKind.Close);
    }

    private static Operand Sma(int period)
    {
        return new Operand(OperandKind.Sma, Period: period);
    }

    private static Operand Constant(double value)
    {
        return new Operand(OperandKind.Constant, Value: value);
    }

    private static RuleSet Rule(Operand left, ComparisonOperator comparison, Operand right)
    {
        return new RuleSet(RuleCombinator.All, new RuleCondition[] { new RuleCondition(left, comparison, right) });
    }

    private static CustomStrategyDefinition Definition(string name, string id, RuleSet rule)
    {
        return new CustomStrategyDefinition(id, name, "", rule, rule);
    }
}
