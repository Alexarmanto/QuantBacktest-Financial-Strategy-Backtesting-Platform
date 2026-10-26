using Microsoft.AspNetCore.Http;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The backtest form (P4-U1) with the raw input of the request (kept as typed, P4-U12), the error of each field
/// (keyed by URL parameter name) and an optional general alert. Built from IStrategyCatalog only.
/// </summary>
internal sealed class BacktestFormViewModel
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
    private static readonly string[] SettingKeys =
    [
        BacktestQuery.SymbolKey, BacktestQuery.TimeframeKey, BacktestQuery.StrategyKey, BacktestQuery.FromKey,
        BacktestQuery.ToKey, BacktestQuery.CapitalKey, BacktestQuery.FeeKey, BacktestQuery.SlippageKey,
        BacktestQuery.StopLossKey, BacktestQuery.TakeProfitKey, BacktestQuery.TrailingStopKey, BacktestQuery.RiskFreeKey,
    ];

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

        // A blank form proposes a symbol and a strategy; once the user has sent something, the input is kept as is.
        var isBlank = query.Count == 0;
        var selected = strategies.FirstOrDefault(s => s.Id == Raw(BacktestQuery.StrategyKey))
            ?? strategies.FirstOrDefault(s => s.Id == PreferredStrategyId)
            ?? strategies.FirstOrDefault();

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BacktestQuery.SymbolKey] = Raw(BacktestQuery.SymbolKey) ?? (isBlank ? cachedSymbols.FirstOrDefault() : null) ?? string.Empty,
            [BacktestQuery.TimeframeKey] = Raw(BacktestQuery.TimeframeKey) ?? BacktestQuery.FormatTimeframe(BacktestQuery.DefaultTimeframe),
            [BacktestQuery.StrategyKey] = selected?.Id ?? string.Empty,
            [BacktestQuery.FromKey] = Raw(BacktestQuery.FromKey) ?? string.Empty,
            [BacktestQuery.ToKey] = Raw(BacktestQuery.ToKey) ?? string.Empty,
            [BacktestQuery.CapitalKey] = Raw(BacktestQuery.CapitalKey) ?? BacktestQuery.FormatNumber(BacktestQuery.DefaultCapital)!,
            [BacktestQuery.FeeKey] = Raw(BacktestQuery.FeeKey) ?? BacktestQuery.FormatNumber(BacktestQuery.DefaultFeePercent)!,
            [BacktestQuery.SlippageKey] = Raw(BacktestQuery.SlippageKey) ?? BacktestQuery.FormatNumber(BacktestQuery.DefaultSlippagePercent)!,
            [BacktestQuery.StopLossKey] = Raw(BacktestQuery.StopLossKey) ?? string.Empty,
            [BacktestQuery.TakeProfitKey] = Raw(BacktestQuery.TakeProfitKey) ?? string.Empty,
            [BacktestQuery.TrailingStopKey] = Raw(BacktestQuery.TrailingStopKey) ?? string.Empty,
            [BacktestQuery.RiskFreeKey] = Raw(BacktestQuery.RiskFreeKey) ?? BacktestQuery.FormatNumber(BacktestQuery.DefaultRiskFreePercent)!,
        };

        // Fields that exist on the page: an error on any other key would be invisible, so it goes to the top.
        // Case-insensitive, like the query collection: "P.fast" in the URL is the p.fast input.
        var fieldNames = new HashSet<string>(SettingKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in selected?.Parameters ?? [])
        {
            fieldNames.Add(BacktestQuery.ParameterPrefix + definition.Key);
        }

        var fieldErrors = errors
            .Where(e => fieldNames.Contains(e.Field))
            .GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(e => e.Message)), StringComparer.OrdinalIgnoreCase);
        var otherErrors = errors.Where(e => !fieldNames.Contains(e.Field)).Select(e => e.Message).ToList();

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

        return new BacktestFormViewModel(
            values,
            fieldErrors,
            otherErrors,
            alert,
            strategies.Where(s => s.Category != StrategyCategory.Custom).Select(ToOption).ToList(),
            strategies.Where(s => s.Category == StrategyCategory.Custom).Select(ToOption).ToList(),
            cachedSymbols);
    }
}
