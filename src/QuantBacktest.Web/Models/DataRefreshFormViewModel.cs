using System.Globalization;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The refresh form of the data pages (P4-U10), posted to /Data/Refresh. On GET /Data the user types the symbol
/// (datalist of the cached symbols) and picks the timeframe; on the quality page the series is fixed (hidden fields).
/// The browser asks <see cref="Confirmation"/> before sending (confirm.js). Implements IFormFields for _FormAlerts.
/// </summary>
internal sealed class DataRefreshFormViewModel : IFormFields
{
    private readonly IReadOnlyDictionary<string, string> _errors;

    private DataRefreshFormViewModel(
        string symbol,
        Timeframe timeframe,
        IReadOnlyList<string> cachedSymbols,
        IReadOnlyDictionary<string, string> errors,
        FormAlert? alert,
        int remainingCalls,
        bool isFixedSeries)
    {
        Symbol = symbol;
        Timeframe = timeframe;
        CachedSymbols = cachedSymbols;
        _errors = errors;
        Alert = alert;
        Confirmation = ConfirmationText(remainingCalls);
        IsFixedSeries = isFixedSeries;
    }

    /// <summary>The symbol as typed, or the symbol of the series.</summary>
    public string Symbol { get; }

    public Timeframe Timeframe { get; }

    /// <summary>URL spelling of the timeframe: the value of the select and of the hidden field.</summary>
    public string TimeframeValue => BacktestQuery.FormatTimeframe(Timeframe);

    public string Confirmation { get; }

    /// <summary>True on the quality page: symbol and tf are hidden fields, only the button is shown.</summary>
    public bool IsFixedSeries { get; }

    /// <summary>The choices of the select, in the order of the rows of GET /Data.</summary>
    public static IReadOnlyList<(string Value, string Label)> TimeframeOptions { get; } =
        DataLabels.Timeframes.Select(t => (BacktestQuery.FormatTimeframe(t), DataLabels.TimeframeLabel(t))).ToList();

    public FormAlert? Alert { get; }

    public bool HasErrors => _errors.Count > 0;

    /// <summary>Both fields have an input: every error is shown next to its field.</summary>
    public IReadOnlyList<string> OtherErrors => [];

    public IReadOnlyList<string> CachedSymbols { get; }

    /// <summary>P4-U10, exact text; remaining is ApiQuotaStatus.Remaining, read by the server.</summary>
    public static string ConfirmationText(int remaining) =>
        $"This uses one of your daily API calls ({remaining.ToString(CultureInfo.InvariantCulture)} left today). Continue?";

    /// <summary>The form of GET /Data, with what the redirect of POST /Data/Refresh brought back.</summary>
    public static DataRefreshFormViewModel ForInput(
        string symbol,
        Timeframe timeframe,
        IReadOnlyList<string> cachedSymbols,
        IReadOnlyDictionary<string, string> fieldErrors,
        FormAlert? alert,
        int remainingCalls)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(cachedSymbols);
        ArgumentNullException.ThrowIfNull(fieldErrors);
        return new DataRefreshFormViewModel(symbol, timeframe, cachedSymbols, fieldErrors, alert, remainingCalls, isFixedSeries: false);
    }

    /// <summary>"Refresh this series" on the quality page; an alert of that page is carried by DataQualityViewModel.</summary>
    public static DataRefreshFormViewModel ForSeries(DataSeriesQuery series, int remainingCalls)
    {
        ArgumentNullException.ThrowIfNull(series);
        return new DataRefreshFormViewModel(
            series.Symbol, series.Timeframe, [], new Dictionary<string, string>(), alert: null, remainingCalls, isFixedSeries: true);
    }

    public string Value(string key) => key switch
    {
        BacktestQuery.SymbolKey => Symbol,
        BacktestQuery.TimeframeKey => TimeframeValue,
        _ => string.Empty,
    };

    public string? Error(string key) => _errors.GetValueOrDefault(key);
}
