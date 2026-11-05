using System.Globalization;
using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One checkbox of the comparator form. Disabled when the strategy does not support the timeframe of the form, with
/// the reason in its label ("daily only"), as on the backtest form (P4-U1); a disabled box is never checked.
/// </summary>
internal sealed record CompareStrategyOption(
    string Id,
    string Name,
    string Timeframes,
    string? Restriction,
    bool IsChecked,
    bool IsDisabled)
{
    public string Label => Restriction is null ? Name : $"{Name} ({Restriction})";

    /// <summary>The id of the input: the strategy ids are kebab-case, so the prefix makes a valid, unique HTML id.</summary>
    public string InputId => "compare-" + Id;
}

/// <summary>
/// The comparator form (P4-X1): the settings fields of the backtest form (same partials, same validation through
/// BacktestQuery) and one checkbox per strategy of IStrategyCatalog, built-in first then custom (StrategyGroups).
/// Prefilled from the URL, raw input kept as typed (P4-U12). Every strategy runs with its default parameters.
/// </summary>
internal sealed class CompareFormViewModel : IFormFields
{
    public const string DefaultParametersNote = "Each strategy runs with its default parameters.";

    private readonly IReadOnlyDictionary<string, string> _values;
    private readonly IReadOnlyDictionary<string, string> _errors;

    private CompareFormViewModel(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string> errors,
        IReadOnlyList<string> otherErrors,
        FormAlert? alert,
        IReadOnlyList<CompareStrategyOption> builtIn,
        IReadOnlyList<CompareStrategyOption> custom,
        IReadOnlyList<string> cachedSymbols)
    {
        _values = values;
        _errors = errors;
        OtherErrors = otherErrors;
        Alert = alert;
        BuiltInStrategies = builtIn;
        CustomStrategies = custom;
        CachedSymbols = cachedSymbols;
    }

    public static int MinStrategies => BacktestQuery.MinComparedStrategies;

    public static int MaxStrategies => BacktestQuery.MaxComparedStrategies;

    public static string TooFewMessage => BacktestQuery.TooFewStrategiesMessage;

    public static string TooManyMessage => BacktestQuery.TooManyStrategiesMessage;

    public FormAlert? Alert { get; }

    public IReadOnlyList<string> OtherErrors { get; }

    public IReadOnlyList<string> CachedSymbols { get; }

    public IReadOnlyList<CompareStrategyOption> BuiltInStrategies { get; }

    public IReadOnlyList<CompareStrategyOption> CustomStrategies { get; }

    public IEnumerable<CompareStrategyOption> AllStrategies => BuiltInStrategies.Concat(CustomStrategies);

    public bool HasErrors => _errors.Count > 0 || OtherErrors.Count > 0;

    /// <summary>Counter of the checked boxes, updated by compare-form.js (aria-live): "2 selected (2 to 5)".</summary>
    public string CountText => FormatCount(AllStrategies.Count(o => o.IsChecked));

    public string? StrategiesError => Error(BacktestQuery.StrategiesKey);

    public string Value(string key) => _values.GetValueOrDefault(key, string.Empty);

    public string? Error(string key) => _errors.GetValueOrDefault(key);

    public static string FormatCount(int count) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} selected "
        + $"({MinStrategies.ToString(CultureInfo.InvariantCulture)} to {MaxStrategies.ToString(CultureInfo.InvariantCulture)})";

    public static CompareFormViewModel Create(
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

        var values = FormSettings.Values(query, cachedSymbols);
        var (fieldErrors, otherErrors) = FormSettings.SplitErrors(errors, [.. FormSettings.Keys, BacktestQuery.StrategiesKey]);

        // A timeframe the form does not know (tf=foo) disables nothing: the field shows its own error.
        var knownTimeframe = BacktestQuery.TryReadTimeframe(values[BacktestQuery.TimeframeKey], out var timeframe);
        var checkedIds = new HashSet<string>(BacktestQuery.ReadStrategyIds(query), StringComparer.Ordinal);

        CompareStrategyOption ToOption(IStrategy strategy)
        {
            var disabled = knownTimeframe && !strategy.SupportedTimeframes.Contains(timeframe);
            return new CompareStrategyOption(
                strategy.Id,
                strategy.Name,
                StrategyOption.FormatTimeframes(strategy),
                StrategyOption.TimeframeRestriction(strategy),
                !disabled && checkedIds.Contains(strategy.Id),
                disabled);
        }

        var (builtIn, custom) = StrategyGroups.Split(strategies);
        return new CompareFormViewModel(
            values,
            fieldErrors,
            otherErrors,
            alert,
            builtIn.Select(ToOption).ToList(),
            custom.Select(ToOption).ToList(),
            cachedSymbols);
    }
}
