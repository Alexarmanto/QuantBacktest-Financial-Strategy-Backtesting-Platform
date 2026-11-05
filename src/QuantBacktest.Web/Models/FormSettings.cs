using Microsoft.AspNetCore.Http;

namespace QuantBacktest.Web.Models;

/// <summary>
/// What the shared partials of the forms (_FormAlerts, _MarketFields, _ExecutionFields) print: the raw value of each
/// settings field (kept as typed, P4-U12), its server error, the general alert and the cached symbols.
/// Implemented by the backtest form (P4-U1) and the comparator form (P4-X1).
/// </summary>
internal interface IFormFields
{
    FormAlert? Alert { get; }

    bool HasErrors { get; }

    /// <summary>Errors whose parameter has no input on the form: listed at the top.</summary>
    IReadOnlyList<string> OtherErrors { get; }

    IReadOnlyList<string> CachedSymbols { get; }

    string Value(string key);

    string? Error(string key);
}

/// <summary>
/// The settings fields every backtest form shares, in URL names, and their displayed value: the raw input when the key
/// is in the URL, the default of BacktestQuery otherwise. The strategy and its parameters are not part of them.
/// </summary>
internal static class FormSettings
{
    /// <summary>Symbol, timeframe, period and execution settings, in display order.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        BacktestQuery.SymbolKey, BacktestQuery.TimeframeKey, BacktestQuery.FromKey, BacktestQuery.ToKey,
        BacktestQuery.CapitalKey, BacktestQuery.FeeKey, BacktestQuery.SlippageKey, BacktestQuery.StopLossKey,
        BacktestQuery.TakeProfitKey, BacktestQuery.TrailingStopKey, BacktestQuery.RiskFreeKey,
    ];

    /// <summary>
    /// A missing symbol key gets the first cached symbol: blank form, or a link that only picks a strategy
    /// ("Use this strategy"). A sent form always has the key, so its input is kept as typed, even empty (P4-U12).
    /// </summary>
    public static Dictionary<string, string> Values(IQueryCollection query, IReadOnlyList<string> cachedSymbols)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(cachedSymbols);

        string? Raw(string key) => query.ContainsKey(key) ? query[key].ToString() : null;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BacktestQuery.SymbolKey] = Raw(BacktestQuery.SymbolKey) ?? cachedSymbols.FirstOrDefault() ?? string.Empty,
            [BacktestQuery.TimeframeKey] = Raw(BacktestQuery.TimeframeKey) ?? BacktestQuery.FormatTimeframe(BacktestQuery.DefaultTimeframe),
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
    }

    /// <summary>
    /// Splits the errors between the fields that exist on the page (joined per field) and the others (listed at the
    /// top). Case-insensitive, like the query collection: "P.fast" in the URL is the p.fast input.
    /// </summary>
    public static (Dictionary<string, string> FieldErrors, List<string> OtherErrors) SplitErrors(
        IReadOnlyList<QueryFieldError> errors,
        IEnumerable<string> fieldNames)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(fieldNames);
        var names = new HashSet<string>(fieldNames, StringComparer.OrdinalIgnoreCase);
        var fieldErrors = errors
            .Where(e => names.Contains(e.Field))
            .GroupBy(e => e.Field, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(e => e.Message)), StringComparer.OrdinalIgnoreCase);
        var otherErrors = errors.Where(e => !names.Contains(e.Field)).Select(e => e.Message).ToList();
        return (fieldErrors, otherErrors);
    }
}
