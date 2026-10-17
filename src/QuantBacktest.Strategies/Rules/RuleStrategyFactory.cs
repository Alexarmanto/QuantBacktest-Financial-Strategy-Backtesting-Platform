using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-N1: checks the rules of a no-code strategy (R1 to R4) and turns them into a RuleBasedStrategy
internal class RuleStrategyFactory : ICustomStrategyFactory
{
    // every problem found, with its position, as English messages for the website (empty = valid)
    public IReadOnlyList<string> Validate(CustomStrategyDefinition definition)
    {
        List<string> problems = new List<string>();

        CheckNameAndId(definition, problems);
        CheckRule("Entry", definition.Entry, problems);
        CheckRule("Exit", definition.Exit, problems);

        return problems;
    }

    // a valid definition gives a strategy, an invalid one gives a BacktestValidationException
    public IStrategy Create(CustomStrategyDefinition definition)
    {
        IReadOnlyList<string> problems = Validate(definition);
        if (problems.Count > 0)
        {
            throw new BacktestValidationException(string.Join(" ", problems));
        }
        return new RuleBasedStrategy(definition);
    }

    // R1: name, description and id
    private static void CheckNameAndId(CustomStrategyDefinition definition, List<string> problems)
    {
        string name = definition.Name.Trim();
        if (name.Length < 1 || name.Length > 60)
        {
            problems.Add("The name must contain 1 to 60 characters.");
        }
        else
        {
            try
            {
                string expectedId = Conventions.CreateCustomStrategyId(name);
                if (definition.Id != expectedId)
                {
                    problems.Add("The id must be '" + expectedId + "'.");
                }
            }
            catch (ArgumentException)
            {
                problems.Add("The name must contain at least one letter or digit.");
            }
        }

        if (definition.Description.Length > 500)
        {
            problems.Add("The description must contain at most 500 characters.");
        }
    }

    // R2 to R4 for the entry rule or the exit rule
    private static void CheckRule(string ruleName, RuleSet rule, List<string> problems)
    {
        // R2: 1 to 5 conditions
        if (rule.Conditions.Count < 1 || rule.Conditions.Count > Conventions.MaxConditionsPerRule)
        {
            problems.Add("The " + ruleName.ToLowerInvariant() + " rule needs 1 to " + Conventions.MaxConditionsPerRule + " conditions.");
        }

        for (int i = 0; i < rule.Conditions.Count; i++)
        {
            RuleCondition condition = rule.Conditions[i];
            string position = ruleName + " condition " + (i + 1) + ": ";

            CheckOperand(position + "the left operand ", condition.Left, problems);
            CheckOperand(position + "the right operand ", condition.Right, problems);

            // R4: comparing two fixed numbers makes no sense
            if (condition.Left.Kind == OperandKind.Constant && condition.Right.Kind == OperandKind.Constant)
            {
                problems.Add(position + "cannot compare two constants.");
            }
        }
    }

    // R3: what each kind of operand needs, and what it must not have
    private static void CheckOperand(string start, Operand operand, List<string> problems)
    {
        // period
        if (NeedsPeriod(operand.Kind))
        {
            int minimum = 1;
            if (operand.Kind == OperandKind.Rsi || IsBollinger(operand.Kind))
            {
                minimum = 2;
            }
            if (operand.Period == null || operand.Period < minimum || operand.Period > 500)
            {
                problems.Add(start + "needs a period between " + minimum + " and 500.");
            }
        }
        else if (operand.Period != null)
        {
            problems.Add(start + "cannot have a period.");
        }

        // value
        if (operand.Kind == OperandKind.Constant)
        {
            if (operand.Value == null || double.IsNaN(operand.Value.Value) || double.IsInfinity(operand.Value.Value))
            {
                problems.Add(start + "needs a finite value.");
            }
        }
        else if (operand.Value != null)
        {
            problems.Add(start + "cannot have a value.");
        }

        // number of standard deviations
        if (IsBollinger(operand.Kind))
        {
            if (operand.StdDevs != null && (operand.StdDevs < 0.5 || operand.StdDevs > 5))
            {
                problems.Add(start + "needs a number of standard deviations between 0.5 and 5.");
            }
        }
        else if (operand.StdDevs != null)
        {
            problems.Add(start + "cannot have a number of standard deviations.");
        }
    }

    private static bool NeedsPeriod(OperandKind kind)
    {
        return kind == OperandKind.Sma || kind == OperandKind.Ema || kind == OperandKind.Rsi
            || kind == OperandKind.RateOfChange || IsBollinger(kind)
            || kind == OperandKind.HighestHigh || kind == OperandKind.LowestLow;
    }

    private static bool IsBollinger(OperandKind kind)
    {
        return kind == OperandKind.BollingerUpper || kind == OperandKind.BollingerMiddle || kind == OperandKind.BollingerLower;
    }
}
