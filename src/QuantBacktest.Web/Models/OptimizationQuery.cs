using System.Globalization;
using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The settings of a heatmap (P4-X2, docs/CONTRACT.md section 6) as they travel in its permalink: the backtest settings
/// (strategy and the p.* of the parameters that are not swept), the two axes and the metric. Built only by
/// <see cref="BacktestQuery.TryParseOptimization"/>; the URL is read and written by BacktestQuery.
/// Only input checks, defaults and unit conversion: IParameterSweepService stays the final authority.
/// </summary>
internal sealed class OptimizationQuery
{
    internal OptimizationQuery(BacktestQuery settings, SweepAxisInput x, SweepAxisInput y, SweepMetric metric)
    {
        Settings = settings;
        X = x;
        Y = y;
        Metric = metric;
    }

    /// <summary>The backtest settings; after <see cref="Resolve"/>, its parameters are the non-swept ones, all explicit.</summary>
    public BacktestQuery Settings { get; }

    public SweepAxisInput X { get; }

    public SweepAxisInput Y { get; }

    public SweepMetric Metric { get; }

    public string StrategyId => Settings.StrategyId;

    public string Symbol => Settings.Symbol;

    public Timeframe Timeframe => Settings.Timeframe;

    /// <summary>
    /// Checks the query against the strategy (O1, as far as the web can tell without the data) and completes it:
    /// at least 2 parameters; x and y known and distinct (absent: the first parameters of the catalog not used by the
    /// other axis); each range in the [Min, Max] of its definition, Min &lt;= Max, Step &gt; 0, whole numbers for an
    /// Integer parameter (absent: P4-X2 defaults); at most Conventions.MaxSweepCells cells; the p.* of the other
    /// parameters as on /Backtest. The p.* of the two swept parameters are dropped: the service replaces them.
    /// </summary>
    /// <returns>The field errors; <paramref name="effective"/> is set only when there is none.</returns>
    public IReadOnlyList<QueryFieldError> Resolve(IStrategy strategy, out OptimizationQuery? effective)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        effective = null;

        if (StrategyChecks.OptimizationError(strategy) is { } tooFew)
        {
            return [new QueryFieldError(BacktestQuery.StrategyKey, tooFew)];
        }

        var definitions = strategy.Parameters;
        var errors = new List<QueryFieldError>();

        var xKey = X.Key ?? definitions.First(d => d.Key != Y.Key).Key;
        var yKey = Y.Key ?? definitions.First(d => d.Key != xKey).Key;
        var xDefinition = Definition(definitions, xKey, BacktestQuery.XKey, errors);
        var yDefinition = Definition(definitions, yKey, BacktestQuery.YKey, errors);
        if (xDefinition is not null && yDefinition is not null && xKey == yKey)
        {
            errors.Add(new QueryFieldError(BacktestQuery.YKey, SameParameterMessage));
        }

        var x = xDefinition is null ? null : CompleteAxis("X", X, xDefinition, BacktestQuery.XMinKey, BacktestQuery.XMaxKey, BacktestQuery.XStepKey, errors);
        var y = yDefinition is null ? null : CompleteAxis("Y", Y, yDefinition, BacktestQuery.YMinKey, BacktestQuery.YMaxKey, BacktestQuery.YStepKey, errors);

        // The other parameters, checked exactly as on /Backtest; the swept ones are replaced by every cell.
        var others = Settings.Parameters
            .Where(p => p.Key != xKey && p.Key != yKey)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var settings = Settings with { Parameters = others };
        errors.AddRange(settings.ValidateParameters(definitions));

        if (errors.Count > 0 || x is null || y is null)
        {
            return errors;
        }

        var xCount = BacktestQuery.AxisValueCount(x.Min!.Value, x.Max!.Value, x.Step!.Value);
        var yCount = BacktestQuery.AxisValueCount(y.Min!.Value, y.Max!.Value, y.Step!.Value);
        if (xCount * yCount > BacktestQuery.MaxSweepCells)
        {
            errors.Add(new QueryFieldError(TooManyCellsField, TooManyCellsMessage(xCount, yCount)));
            errors.Add(new QueryFieldError(BacktestQuery.XStepKey, TooManyCellsHint));
            errors.Add(new QueryFieldError(BacktestQuery.YStepKey, TooManyCellsHint));
            return errors;
        }

        var otherDefinitions = definitions.Where(d => d.Key != xKey && d.Key != yKey).ToList();
        effective = new OptimizationQuery(settings.WithDefaultParameters(otherDefinitions), x, y, Metric);
        return [];
    }

    /// <summary>Not a field of the form: the error is listed at the top of the page.</summary>
    public const string TooManyCellsField = "cells";

    public const string SameParameterMessage = "The X and Y parameters must be different.";

    public const string TooManyCellsHint = "Increase this step or narrow the range.";

    public static string TooManyCellsMessage(double xCount, double yCount) =>
        $"The heatmap would have {Format(xCount * yCount)} cells ({Format(xCount)} × {Format(yCount)}); "
        + $"the maximum is {BacktestQuery.FormatInteger(BacktestQuery.MaxSweepCells)}. Increase a step or narrow a range.";

    public static string UnknownParameterMessage(string key) => $"Unknown parameter '{key}' for this strategy.";

    /// <summary>The request of the contract: one SweepRequest, with the URL values converted to the doubles of SweepAxis.</summary>
    public SweepRequest ToSweepRequest()
    {
        RequireComplete();
        return new SweepRequest(Settings.ToBacktestRequest(), Axis(X), Axis(Y), Metric);
    }

    /// <summary>
    /// The backtest of one cell (P4-X2, "clic sur une cellule → résultat détaillé avec ces paramètres"): these settings
    /// with the two swept parameters set to the cell values. Its ToQueryString() is canonical for /Backtest/Result.
    /// </summary>
    public BacktestQuery ForCell(double x, double y)
    {
        RequireComplete();
        var parameters = new Dictionary<string, decimal>(Settings.Parameters, StringComparer.Ordinal)
        {
            [X.Key!] = BacktestQuery.ParameterValue(x),
            [Y.Key!] = BacktestQuery.ParameterValue(y),
        };
        return Settings with { Parameters = parameters };
    }

    /// <summary>Canonical query string of /Optimize/Result (and of the prefilled form), without the leading "?".</summary>
    public string ToQueryString() => Settings.ToOptimizationQueryString(X, Y, Metric);

    public bool IsCanonical(QueryString received) => Settings.IsCanonicalOptimization(received, X, Y, Metric);

    private static SweepAxis Axis(SweepAxisInput axis) =>
        new(axis.Key!, (double)axis.Min!.Value, (double)axis.Max!.Value, (double)axis.Step!.Value);

    private void RequireComplete()
    {
        if (!X.IsComplete || !Y.IsComplete)
        {
            throw new InvalidOperationException("Call Resolve first: the axes are not complete.");
        }
    }

    private static ParameterDefinition? Definition(
        IReadOnlyList<ParameterDefinition> definitions,
        string key,
        string field,
        List<QueryFieldError> errors)
    {
        var definition = definitions.FirstOrDefault(d => d.Key == key);
        if (definition is null)
        {
            errors.Add(new QueryFieldError(field, UnknownParameterMessage(key)));
        }

        return definition;
    }

    private static SweepAxisInput? CompleteAxis(
        string axis,
        SweepAxisInput input,
        ParameterDefinition definition,
        string minKey,
        string maxKey,
        string stepKey,
        List<QueryFieldError> errors)
    {
        var min = input.Min ?? BacktestQuery.ParameterValue(definition.Min);
        var max = input.Max ?? BacktestQuery.ParameterValue(definition.Max);
        var step = input.Step ?? BacktestQuery.DefaultSweepStep(definition);
        var before = errors.Count;
        var range = $"{BacktestQuery.FormatNumber(definition.Min)} and {BacktestQuery.FormatNumber(definition.Max)}";

        void CheckBound(string key, string label, decimal value)
        {
            if ((double)value < definition.Min || (double)value > definition.Max)
            {
                errors.Add(new QueryFieldError(key, $"{axis} {label} must be between {range} ({definition.Label})."));
            }
        }

        CheckBound(minKey, "minimum", min);
        CheckBound(maxKey, "maximum", max);
        if (min > max)
        {
            errors.Add(new QueryFieldError(minKey, $"{axis} minimum must not be greater than {axis} maximum."));
        }

        if (step <= 0)
        {
            errors.Add(new QueryFieldError(stepKey, $"{axis} step must be greater than 0."));
        }

        if (definition.Type == ParameterType.Integer)
        {
            foreach (var (key, label, value) in new[] { (minKey, "minimum", min), (maxKey, "maximum", max), (stepKey, "step", step) })
            {
                if (value != decimal.Truncate(value))
                {
                    errors.Add(new QueryFieldError(key, $"{axis} {label} must be a whole number ({definition.Label} takes whole numbers)."));
                }
            }
        }

        return errors.Count > before ? null : new SweepAxisInput(definition.Key, min, max, step);
    }

    private static string Format(double count) => count.ToString("0", CultureInfo.InvariantCulture);
}
