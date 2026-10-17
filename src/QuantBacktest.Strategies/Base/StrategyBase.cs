using System.Globalization;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies;

// P2-C2: common base class of every strategy (like Instrument in TP2).
// It keeps the information of the strategy and gives small helpers to the child classes.
public abstract class StrategyBase : IStrategy
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public StrategyCategory Category { get; }
    public StrategyDocumentation Documentation { get; }
    public IReadOnlyList<Timeframe> SupportedTimeframes { get; }
    public IReadOnlyList<ParameterDefinition> Parameters { get; }

    // Constructor, called by each strategy with base(...)
    protected StrategyBase(
        string id,
        string name,
        string description,
        StrategyCategory category,
        StrategyDocumentation documentation,
        Timeframe[] supportedTimeframes,
        ParameterDefinition[] parameters)
    {
        Id = id;
        Name = name;
        Description = description;
        Category = category;
        Documentation = documentation;
        SupportedTimeframes = supportedTimeframes;
        Parameters = parameters;
    }

    // each strategy writes its own rules
    public abstract IReadOnlyList<Signal> GenerateSignals(
        IReadOnlyList<PriceBar> bars,
        IReadOnlyDictionary<string, double> parameters);

    // each strategy says which lines to draw on the charts
    public abstract IReadOnlyList<IndicatorSeries> ComputeIndicators(
        IReadOnlyList<PriceBar> bars,
        IReadOnlyDictionary<string, double> parameters);

    // S5: value of a parameter, or its default value when it is missing
    protected double ReadParameter(IReadOnlyDictionary<string, double> parameters, string key)
    {
        if (parameters.ContainsKey(key))
        {
            return parameters[key];
        }

        foreach (ParameterDefinition definition in Parameters)
        {
            if (definition.Key == key)
            {
                return definition.DefaultValue;
            }
        }

        throw new InvalidOperationException($"Unknown parameter '{key}' in strategy '{Id}'.");
    }

    // same thing for a whole number (a period in bars for example)
    protected int ReadIntParameter(IReadOnlyDictionary<string, double> parameters, string key)
    {
        return (int)Math.Round(ReadParameter(parameters, key));
    }

    // S5: a wrong relation between two parameters is refused
    protected static void CheckRelation(bool isValid, string message)
    {
        if (!isValid)
        {
            throw new BacktestValidationException(message);
        }
    }

    protected static double[] GetOpens(IReadOnlyList<PriceBar> bars)
    {
        double[] result = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            result[i] = (double)bars[i].Open;
        }
        return result;
    }

    protected static double[] GetHighs(IReadOnlyList<PriceBar> bars)
    {
        double[] result = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            result[i] = (double)bars[i].High;
        }
        return result;
    }

    protected static double[] GetLows(IReadOnlyList<PriceBar> bars)
    {
        double[] result = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            result[i] = (double)bars[i].Low;
        }
        return result;
    }

    protected static double[] GetCloses(IReadOnlyList<PriceBar> bars)
    {
        double[] result = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            result[i] = (double)bars[i].Close;
        }
        return result;
    }

    protected static double[] GetVolumes(IReadOnlyList<PriceBar> bars)
    {
        double[] result = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            result[i] = bars[i].Volume;
        }
        return result;
    }

    // one line to draw on a chart (S6: NaN becomes null)
    protected static IndicatorSeries MakeSeries(string key, string label, IndicatorPane pane, double[] values, double[] referenceLines)
    {
        return new IndicatorSeries(key, label, pane, Indicators.ToNullable(values), referenceLines);
    }

    // a number written with a dot, whatever the language of the computer (2.5, not 2,5)
    protected static string Format(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
