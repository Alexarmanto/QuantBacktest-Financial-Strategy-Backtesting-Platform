using Microsoft.AspNetCore.Http;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The backtest form (P4-U1) with the raw input of the request (kept as typed, P4-U12), the error of each field
/// (keyed by URL parameter name) and an optional general alert. Built from IStrategyCatalog only.
/// </summary>
internal sealed class BacktestFormViewModel : IFormFields
{
    /// <summary>Strategy preselected on a blank form when the catalog has it (built-in, docs/CONTRACT.md section 8).</summary>
    public const string PreferredStrategyId = "sma-crossover";

    /// <summary>
    /// Client-side twin of Conventions.SymbolPattern for the pattern attribute: the server upper-cases before
    /// checking, so lower case is accepted here too. The browser anchors the pattern itself.
    /// </summary>
    public static readonly string SymbolInputPattern =
        Conventions.SymbolPattern.TrimStart('^').TrimEnd('$').Replace("A-Z", "A-Za-z", StringComparison.Ordinal);

    /// <summary>Settings fields of the form, in display order. Strategy parameters are in <see cref="StrategyOption"/>.</summary>
    private static readonly string[] SettingKeys = [.. FormSettings.Keys, BacktestQuery.StrategyKey];

    private readonly IReadOnlyDictionary<string, string> _values;
    private readonly IReadOnlyDictionary<string, string> _errors;

    private BacktestFormViewModel(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, string> errors,
        IReadOnlyList<string> otherErrors,
        FormAlert? alert,
        IReadOnlyList<StrategyOption> strategies,
        IReadOnlyList<StrategyOption> customStrategies,
        IReadOnlyList<string> cachedSymbols)
    {
        _values = values;
        _errors = errors;
        OtherErrors = otherErrors;
        Alert = alert;
        BuiltInStrategies = strategies;
        CustomStrategies = customStrategies;
        CachedSymbols = cachedSymbols;
    }

    /// <summary>Errors whose parameter has no input on the form (for example p.unknown): listed at the top.</summary>
    public IReadOnlyList<string> OtherErrors { get; }

    public FormAlert? Alert { get; }

    /// <summary>Built-in strategies first, then custom ones (P4-U1), each in catalog order.</summary>
    public IReadOnlyList<StrategyOption> BuiltInStrategies { get; }

    public IReadOnlyList<StrategyOption> CustomStrategies { get; }

    public IEnumerable<StrategyOption> AllStrategies => BuiltInStrategies.Concat(CustomStrategies);

    public IReadOnlyList<string> CachedSymbols { get; }

    public bool HasErrors => _errors.Count > 0 || OtherErrors.Count > 0;

    /// <summary>Value of a settings field, as typed or as its default.</summary>
    public string Value(string key) => _values.GetValueOrDefault(key, string.Empty);

    public string? Error(string key) => _errors.GetValueOrDefault(key);

    public static BacktestFormViewModel Create(
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

        var selected = strategies.FirstOrDefault(s => s.Id == Raw(BacktestQuery.StrategyKey))
            ?? strategies.FirstOrDefault(s => s.Id == PreferredStrategyId)
            ?? strategies.FirstOrDefault();

        var values = FormSettings.Values(query, cachedSymbols);
        values[BacktestQuery.StrategyKey] = selected?.Id ?? string.Empty;

        // Fields that exist on the page: an error on any other key would be invisible, so it goes to the top.
        var fieldNames = SettingKeys.Concat((selected?.Parameters ?? []).Select(d => BacktestQuery.ParameterPrefix + d.Key));
        var (fieldErrors, otherErrors) = FormSettings.SplitErrors(errors, fieldNames);

        StrategyOption ToOption(IStrategy strategy)
        {
            var isSelected = strategy == selected;
            return new StrategyOption(
                strategy.Id,
                strategy.Name,
                StrategyOption.FormatTimeframes(strategy),
                StrategyOption.TimeframeRestriction(strategy),
                isSelected,
                StrategyOption.CreateFields(
                    strategy,
                    name => isSelected ? Raw(name) : null,
                    name => isSelected ? fieldErrors.GetValueOrDefault(name) : null));
        }

        var (builtIn, custom) = StrategyGroups.Split(strategies);
        return new BacktestFormViewModel(
            values,
            fieldErrors,
            otherErrors,
            alert,
            builtIn.Select(ToOption).ToList(),
            custom.Select(ToOption).ToList(),
            cachedSymbols);
    }
}
