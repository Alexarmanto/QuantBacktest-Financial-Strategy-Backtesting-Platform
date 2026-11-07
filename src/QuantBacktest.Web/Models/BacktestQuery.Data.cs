using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using QuantBacktest.Core;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The URL part of the data pages (P4-U10, PART-4 section 5): symbol (required, upper-cased) and tf (daily by default),
/// for GET /Data/Quality, the form of POST /Data/Refresh and the prefill of GET /Data. Same Reader, same wording.
/// </summary>
internal sealed partial record BacktestQuery
{
    // Same wording as ReadSettings, so a symbol or timeframe error reads the same on every page.
    public const string SymbolRequiredMessage = "Symbol is required.";
    public const string SymbolFormatMessage = "Symbol must be 1 to 10 characters: letters, digits, '.' or '-'.";
    public const string TimeframeFormatMessage = "Timeframe must be 'daily' or 'weekly'.";

    /// <summary>
    /// Reads symbol and tf of a data page. symbol absent, blank, repeated or outside Conventions.SymbolPattern (after
    /// trim and upper case) and a tf other than daily or weekly (any case) are field errors; tf absent or blank is
    /// daily (PART-4 section 5). Unknown parameters are ignored. Never throws on user input.
    /// </summary>
    public static bool TryParseDataSeries(
        IQueryCollection query,
        [NotNullWhen(true)] out DataSeriesQuery? result,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reader = new Reader(query);

        var symbol = reader.Text(SymbolKey, "Symbol")?.ToUpperInvariant();
        if (symbol is null)
        {
            reader.ErrorIfNone(SymbolKey, SymbolRequiredMessage);
        }
        else if (!Regex.IsMatch(symbol, Conventions.SymbolPattern, RegexOptions.CultureInvariant))
        {
            reader.Error(SymbolKey, SymbolFormatMessage);
        }

        var timeframe = DefaultTimeframe;
        var timeframeText = reader.Text(TimeframeKey, "Timeframe");
        if (timeframeText is not null && !Timeframes.TryGetValue(timeframeText, out timeframe))
        {
            reader.Error(TimeframeKey, TimeframeFormatMessage);
        }

        errors = reader.Errors;
        if (errors.Count > 0)
        {
            result = null;
            return false;
        }

        result = new DataSeriesQuery(symbol!, timeframe);
        return true;
    }

    /// <summary>The refresh form (POST /Data/Refresh): exactly the rules of <see cref="TryParseDataSeries"/>, on the posted fields.</summary>
    public static bool TryParseRefresh(
        IFormCollection form,
        [NotNullWhen(true)] out DataSeriesQuery? result,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(form);
        return TryParseDataSeries(AsQuery(form), out result, out errors);
    }

    /// <summary>
    /// What the refresh form of GET /Data shows: the symbol as typed (trimmed, not checked: an error from the redirect
    /// is shown next to it) and tf when it is daily or weekly, daily otherwise. Unknown parameters are ignored.
    /// </summary>
    public static (string Symbol, Timeframe Timeframe) ReadRefreshPrefill(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var symbol = query[SymbolKey].ToString().Trim();
        return TryReadTimeframe(query[TimeframeKey].ToString(), out var timeframe) ? (symbol, timeframe) : (symbol, DefaultTimeframe);
    }

    /// <summary>
    /// Query string, without the leading "?", that sends the posted input back to the form of GET /Data: symbol and tf as
    /// typed (trimmed), each only when not blank. Empty when both are blank.
    /// </summary>
    public static string ToRefreshPrefillQueryString(IFormCollection form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var key in new[] { SymbolKey, TimeframeKey })
        {
            var value = form[key].ToString().Trim();
            if (value.Length > 0)
            {
                pairs.Add(new(key, value));
            }
        }

        return Encode(pairs);
    }

    /// <summary>Canonical query string of a data page, without the leading "?": symbol, then tf ("daily" or "weekly").</summary>
    public static string ToDataQueryString(DataSeriesQuery series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return Encode([new(SymbolKey, series.Symbol), new(TimeframeKey, FormatTimeframe(series.Timeframe))]);
    }

    /// <summary>True when the received query string is exactly <see cref="ToDataQueryString"/> once both are decoded.</summary>
    public static bool IsCanonicalData(QueryString received, DataSeriesQuery series) =>
        DecodePairs(received.Value).SequenceEqual(DecodePairs(ToDataQueryString(series)));

    /// <summary>The posted fields as a query collection, so the form is read by the same Reader as a URL.</summary>
    private static QueryCollection AsQuery(IFormCollection form)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in form)
        {
            values[key] = value;
        }

        return new QueryCollection(values);
    }
}
