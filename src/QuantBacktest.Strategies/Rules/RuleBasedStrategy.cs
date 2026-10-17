using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// R5 to R8: a strategy built from the rules chosen by the user on the website (no code).
// It works like the 11 built-in strategies: one signal per bar, Buy, Sell or Hold.
// Its constructor has a parameter, so the catalog does not list it as a built-in strategy.
internal class RuleBasedStrategy : StrategyBase
{
    private readonly CustomStrategyDefinition _definition;

    // Constructor
    public RuleBasedStrategy(CustomStrategyDefinition definition)
        : base(
            definition.Id,
            definition.Name,
            definition.Description,
            StrategyCategory.Custom,
            MakeDocumentation(definition),
            new Timeframe[] { Timeframe.Daily, Timeframe.Weekly },
            new ParameterDefinition[0])
    {
        _definition = definition;
    }

    public override IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        bool[] entry = IsRuleTrue(_definition.Entry, bars);
        bool[] exit = IsRuleTrue(_definition.Exit, bars);

        // R7: entry true -> Buy, exit true -> Sell, both true -> Sell (S4)
        Signal[] signals = new Signal[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            if (exit[i])
            {
                signals[i] = Signal.Sell;
            }
            else if (entry[i])
            {
                signals[i] = Signal.Buy;
            }
        }
        return signals;
    }

    // P2-N3: one line per different indicator used in the rules
    public override IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
    {
        List<IndicatorSeries> series = new List<IndicatorSeries>();
        List<string> keysAlreadyAdded = new List<string>();

        foreach (Operand operand in AllOperands())
        {
            string key = OperandValues.Key(operand);
            if (!OperandValues.IsDrawn(operand) || keysAlreadyAdded.Contains(key))
            {
                continue;
            }

            double[] values = Compute(operand, bars);
            series.Add(MakeSeries(key, OperandValues.Label(operand), OperandValues.Pane(operand), values, new double[0]));
            keysAlreadyAdded.Add(key);
        }

        return series;
    }

    // R2: "All" = every condition must be true (AND), "Any" = at least one (OR)
    private static bool[] IsRuleTrue(RuleSet rule, IReadOnlyList<PriceBar> bars)
    {
        bool[] result = new bool[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            result[i] = rule.Combinator == RuleCombinator.All;
        }

        foreach (RuleCondition condition in rule.Conditions)
        {
            double[] left = Compute(condition.Left, bars);
            double[] right = Compute(condition.Right, bars);

            for (int i = 0; i < bars.Count; i++)
            {
                bool isTrue = IsConditionTrue(condition.Operator, left, right, i);
                if (rule.Combinator == RuleCombinator.All)
                {
                    result[i] = result[i] && isTrue;
                }
                else
                {
                    result[i] = result[i] || isTrue;
                }
            }
        }

        return result;
    }

    // R6: a condition is false when one of the values it needs is not defined (warm-up)
    private static bool IsConditionTrue(ComparisonOperator comparison, double[] left, double[] right, int i)
    {
        switch (comparison)
        {
            case ComparisonOperator.GreaterThan:
                return !double.IsNaN(left[i]) && !double.IsNaN(right[i]) && left[i] > right[i];
            case ComparisonOperator.LessThan:
                return !double.IsNaN(left[i]) && !double.IsNaN(right[i]) && left[i] < right[i];
            case ComparisonOperator.CrossesAbove:
                return Indicators.CrossesAbove(left, right, i);
            case ComparisonOperator.CrossesBelow:
                return Indicators.CrossesBelow(left, right, i);
            default:
                return false;
        }
    }

    private static double[] Compute(Operand operand, IReadOnlyList<PriceBar> bars)
    {
        return OperandValues.Compute(operand, GetOpens(bars), GetHighs(bars), GetLows(bars), GetCloses(bars), GetVolumes(bars));
    }

    // the operands of the entry rule then of the exit rule, left then right, in order
    private List<Operand> AllOperands()
    {
        List<Operand> operands = new List<Operand>();
        foreach (RuleCondition condition in _definition.Entry.Conditions)
        {
            operands.Add(condition.Left);
            operands.Add(condition.Right);
        }
        foreach (RuleCondition condition in _definition.Exit.Conditions)
        {
            operands.Add(condition.Left);
            operands.Add(condition.Right);
        }
        return operands;
    }

    // P2-N2: the explanation card is written from the rules
    private static StrategyDocumentation MakeDocumentation(CustomStrategyDefinition definition)
    {
        string idea = definition.Description;
        if (string.IsNullOrWhiteSpace(idea))
        {
            idea = "Custom no-code strategy.";
        }

        return new StrategyDocumentation(
            idea,
            "Buy when " + RuleText(definition.Entry),
            "Sell when " + RuleText(definition.Exit),
            "User-defined rules. Check the train/test page before trusting the results.");
    }

    // "ALL of: Close > SMA(200); RSI(2) < 10"
    private static string RuleText(RuleSet rule)
    {
        string text = "ALL of: ";
        if (rule.Combinator == RuleCombinator.Any)
        {
            text = "ANY of: ";
        }

        for (int i = 0; i < rule.Conditions.Count; i++)
        {
            if (i > 0)
            {
                text = text + "; ";
            }
            RuleCondition condition = rule.Conditions[i];
            text = text + OperandValues.Label(condition.Left) + " " + OperatorText(condition.Operator) + " " + OperandValues.Label(condition.Right);
        }
        return text;
    }

    private static string OperatorText(ComparisonOperator comparison)
    {
        switch (comparison)
        {
            case ComparisonOperator.GreaterThan: return ">";
            case ComparisonOperator.LessThan: return "<";
            case ComparisonOperator.CrossesAbove: return "crosses above";
            default: return "crosses below";
        }
    }
}
