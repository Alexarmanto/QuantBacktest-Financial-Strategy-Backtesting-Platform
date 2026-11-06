using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>A p.* input of the heatmap form; the inputs of the two swept parameters are disabled (not sent).</summary>
internal sealed record OptimizeParameterField(string Key, ParameterField Field, bool IsSwept);

/// <summary>
/// One entry of the strategy list of the heatmap form and, for a strategy that can be optimised, the fieldset of its
/// axes and other parameters. Ineligible (fewer than 2 parameters, O1) is the reason the option is disabled.
/// </summary>
internal sealed record OptimizeStrategyOption(
    string Id,
    string Name,
    string Timeframes,
    string? Restriction,
    string? Ineligible,
    bool IsSelected,
    SweepAxisField? X,
    SweepAxisField? Y,
    IReadOnlyList<OptimizeParameterField> Parameters)
{
    public string Label => (Restriction, Ineligible) switch
    {
        (_, { } reason) => $"{Name} ({reason})",
        ({ } restriction, null) => $"{Name} ({restriction})",
        _ => Name,
    };

    /// <summary>Only a strategy with more than 2 parameters has parameters that stay fixed whatever the axes.</summary>
    public bool HasOtherParameters => Parameters.Count > 2;
}

/// <summary>One option of the metric select.</summary>
internal sealed record MetricOption(string Token, string Label, bool IsSelected);

/// <summary>
/// The heatmap form (P4-X2): the settings fields of the backtest form (same partials, read by the same BacktestQuery
/// code), a strategy with at least 2 parameters, its X and Y axes with their ranges, its other parameters and the
/// metric. Built from IStrategyCatalog only; prefilled from the URL, raw input kept as typed (P4-U12).
/// </summary>
internal sealed class OptimizeFormViewModel : IFormFields
{
    private readonly IReadOnlyDictionary<string, string> _values;
    private readonly IReadOnlyDictionary<string, string> _errors;

    private OptimizeFormViewModel(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string> errors,
        IReadOnlyList<string> otherErrors,
        FormAlert? alert,
        IReadOnlyList<OptimizeStrategyOption> builtIn,
        IReadOnlyList<OptimizeStrategyOption> custom,
        IReadOnlyList<MetricOption> metrics,
        IReadOnlyList<string> cachedSymbols)
    {
        _values = values;
        _errors = errors;
        OtherErrors = otherErrors;
        Alert = alert;
        BuiltInStrategies = builtIn;
        CustomStrategies = custom;
        Metrics = metrics;
        CachedSymbols = cachedSymbols;
    }

    public static int MaxCells => BacktestQuery.MaxSweepCells;

    public FormAlert? Alert { get; }

    public IReadOnlyList<string> OtherErrors { get; }

    public IReadOnlyList<string> CachedSymbols { get; }

    public IReadOnlyList<OptimizeStrategyOption> BuiltInStrategies { get; }

    public IReadOnlyList<OptimizeStrategyOption> CustomStrategies { get; }

    public IEnumerable<OptimizeStrategyOption> AllStrategies => BuiltInStrategies.Concat(CustomStrategies);

    public OptimizeStrategyOption? Selected => AllStrategies.FirstOrDefault(o => o.IsSelected);

    public IReadOnlyList<MetricOption> Metrics { get; }

    public bool HasErrors => _errors.Count > 0 || OtherErrors.Count > 0;

    public string Value(string key) => _values.GetValueOrDefault(key, string.Empty);

    public string? Error(string key) => _errors.GetValueOrDefault(key);

    public static OptimizeFormViewModel Create(
        IQueryCollection query,
        IReadOnlyList<IStrategy> strategies,
        IReadOnlyList<string> cachedSymbols,
        IReadOnlyList<QueryFieldError> errors,
        FormAlert? alert)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(cachedSymbols);
        ArgumentNullException.ThrowIfNull(errors);

        string? Raw(string key) => query.ContainsKey(key) ? query[key].ToString() : null;

        var values = FormSettings.Values(query, cachedSymbols);
        var knownTimeframe = BacktestQuery.TryReadTimeframe(values[BacktestQuery.TimeframeKey], out var timeframe);
        var selected = SelectStrategy(strategies, Raw(BacktestQuery.StrategyKey), knownTimeframe ? timeframe : null);
        values[BacktestQuery.StrategyKey] = selected?.Id ?? string.Empty;

        var fieldNames = FormSettings.Keys
            .Concat([BacktestQuery.StrategyKey, BacktestQuery.MetricKey])
            .Concat(selected is null ? [] : BacktestQuery.SweepKeys)
            .Concat((selected?.Parameters ?? []).Select(d => BacktestQuery.ParameterPrefix + d.Key));
        var (fieldErrors, otherErrors) = FormSettings.SplitErrors(errors, fieldNames);

        OptimizeStrategyOption ToOption(IStrategy strategy)
        {
            var isSelected = strategy == selected;
            var ineligible = StrategyChecks.OptimizationError(strategy) is null ? null : StrategyChecks.TooFewParametersRestriction;
            if (ineligible is not null)
            {
                return new OptimizeStrategyOption(
                    strategy.Id, strategy.Name, StrategyOption.FormatTimeframes(strategy), StrategyOption.TimeframeRestriction(strategy),
                    ineligible, false, null, null, []);
            }

            string? RawOf(string name) => isSelected ? Raw(name) : null;
            string? ErrorOf(string name) => isSelected ? fieldErrors.GetValueOrDefault(name) : null;

            var definitions = strategy.Parameters;
            var rawX = RawOf(BacktestQuery.XKey)?.Trim();
            var rawY = RawOf(BacktestQuery.YKey)?.Trim();
            var xKey = definitions.Any(d => d.Key == rawX) ? rawX! : definitions.First(d => d.Key != rawY).Key;
            var yKey = definitions.Any(d => d.Key == rawY) ? rawY! : definitions.First(d => d.Key != xKey).Key;

            SweepAxisField Axis(string axis, string name, string selectedKey, string minKey, string maxKey, string stepKey)
            {
                var choice = SweepAxisChoice.From(definitions.First(d => d.Key == selectedKey));
                // The step has no HTML bound: Step > 0 is checked by the server (and the browser cannot say "> 0").
                SweepRangeInput Range(string key, string label, string fallback, bool bounded = true) => new(
                    key,
                    $"{strategy.Id}-{key}",
                    label,
                    RawOf(key) ?? fallback,
                    bounded ? choice.Min : null,
                    bounded ? choice.Max : null,
                    choice.HtmlStep,
                    ErrorOf(key));

                return new SweepAxisField(
                    axis,
                    name,
                    $"{strategy.Id}-{name}",
                    selectedKey,
                    definitions.Select(SweepAxisChoice.From).ToList(),
                    ErrorOf(name),
                    Range(minKey, "Min", choice.Min),
                    Range(maxKey, "Max", choice.Max),
                    Range(stepKey, "Step", choice.DefaultStep, bounded: false));
            }

            var parameters = StrategyOption.CreateFields(strategy, RawOf, ErrorOf)
                .Zip(definitions, (field, definition) => new OptimizeParameterField(
                    definition.Key, field, definition.Key == xKey || definition.Key == yKey))
                .ToList();

            return new OptimizeStrategyOption(
                strategy.Id,
                strategy.Name,
                StrategyOption.FormatTimeframes(strategy),
                StrategyOption.TimeframeRestriction(strategy),
                null,
                isSelected,
                Axis("X", BacktestQuery.XKey, xKey, BacktestQuery.XMinKey, BacktestQuery.XMaxKey, BacktestQuery.XStepKey),
                Axis("Y", BacktestQuery.YKey, yKey, BacktestQuery.YMinKey, BacktestQuery.YMaxKey, BacktestQuery.YStepKey),
                parameters);
        }

        var rawMetric = Raw(BacktestQuery.MetricKey);
        var metric = BacktestQuery.TryReadMetric(rawMetric, out var parsed) ? parsed : BacktestQuery.DefaultMetric;
        values[BacktestQuery.MetricKey] = BacktestQuery.FormatMetric(metric);
        var metrics = BacktestQuery.SweepMetricsInOrder
            .Select(m => new MetricOption(BacktestQuery.FormatMetric(m), SweepMetrics.Label(m), m == metric))
            .ToList();

        var (builtIn, custom) = StrategyGroups.Split(strategies);
        return new OptimizeFormViewModel(
            values,
            fieldErrors,
            otherErrors,
            alert,
            builtIn.Select(ToOption).ToList(),
            custom.Select(ToOption).ToList(),
            metrics,
            cachedSymbols);
    }

    /// <summary>
    /// The strategy of the URL when it can be optimised; otherwise the preferred one of the backtest form, then the
    /// first one that supports the timeframe of the form, then the first one with at least 2 parameters.
    /// </summary>
    private static IStrategy? SelectStrategy(IReadOnlyList<IStrategy> strategies, string? rawId, Timeframe? timeframe)
    {
        var eligible = strategies.Where(s => StrategyChecks.OptimizationError(s) is null).ToList();
        bool Supports(IStrategy s) => timeframe is not { } tf || s.SupportedTimeframes.Contains(tf);

        return eligible.FirstOrDefault(s => s.Id == rawId?.Trim())
            ?? eligible.FirstOrDefault(s => s.Id == BacktestFormViewModel.PreferredStrategyId && Supports(s))
            ?? eligible.FirstOrDefault(Supports)
            ?? eligible.FirstOrDefault();
    }
}
