using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>One input error, keyed by the URL parameter name ("capital", "p.fast") so the form can show it next to its field.</summary>
internal sealed record QueryFieldError(string Field, string Message);

/// <summary>
/// The settings of one backtest as they travel in a permalink (PART-4 section 5, P4-U16): the only place that knows
/// the URL parameter names. Values are kept in URL units (rates in percent); <see cref="ToBacktestRequest"/> converts
/// them to the fractions of the contract. Only parsing, input validation and unit conversion: no financial logic.
/// </summary>
internal sealed record BacktestQuery(
    string Symbol,
    Timeframe Timeframe,
    string StrategyId,
    DateOnly? From,
    DateOnly? To,
    decimal Capital,
    decimal FeePercent,
    decimal SlippagePercent,
    decimal? StopLossPercent,
    decimal? TakeProfitPercent,
    decimal? TrailingStopPercent,
    decimal RiskFreePercent,
    IReadOnlyDictionary<string, decimal> Parameters)
{
    public const string SymbolKey = "symbol";
    public const string TimeframeKey = "tf";
    public const string StrategyKey = "strategy";
    public const string FromKey = "from";
    public const string ToKey = "to";
    public const string CapitalKey = "capital";
    public const string FeeKey = "fee";
    public const string SlippageKey = "slip";
    public const string StopLossKey = "sl";
    public const string TakeProfitKey = "tp";
    public const string TrailingStopKey = "ts";
    public const string RiskFreeKey = "rf";
    public const string ParameterPrefix = "p.";

    public const string DateFormat = "yyyy-MM-dd";

    // Defaults and limits come from Conventions (fractions), shown here in URL units (percent).
    // Conventions has no default timeframe: daily is the default of PART-4 section 5.
    public const Timeframe DefaultTimeframe = Timeframe.Daily;
    public const decimal DefaultCapital = Conventions.DefaultInitialCapital;
    public const decimal DefaultFeePercent = Conventions.DefaultFeeRate * 100m;
    public const decimal DefaultSlippagePercent = Conventions.DefaultSlippageRate * 100m;
    public static readonly decimal DefaultRiskFreePercent = (decimal)Conventions.DefaultRiskFreeRate * 100m;

    // Public so the form can show the same ranges as min / max attributes (P4-U1).
    public const decimal MaxFeePercent = Conventions.MaxFeeRate * 100m;
    public const decimal MaxSlippagePercent = Conventions.MaxSlippageRate * 100m;
    public const decimal MaxStopLossPercent = Conventions.MaxStopLossFraction * 100m;
    public const decimal MaxTakeProfitPercent = Conventions.MaxTakeProfitFraction * 100m;
    public const decimal MaxTrailingStopPercent = Conventions.MaxTrailingStopFraction * 100m;
    public static readonly decimal MaxRiskFreePercent = (decimal)Conventions.MaxRiskFreeRate * 100m;

    /// <summary>Accepted spellings of tf (case-insensitive). An explicit table, so "0" or "1" are never read as enum values.</summary>
    private static readonly IReadOnlyDictionary<string, Timeframe> Timeframes =
        new Dictionary<string, Timeframe>(StringComparer.OrdinalIgnoreCase)
        {
            ["daily"] = Timeframe.Daily,
            ["weekly"] = Timeframe.Weekly,
        };

    /// <summary>Plain decimal notation only: no thousands separator, no exponent, "." as decimal point.</summary>
    private const NumberStyles NumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>Shortest invariant form of a decimal: 10000.00 → "10000", 0.10 → "0.1", never an exponent.</summary>
    private const string ShortestDecimal = "0.############################";

    /// <summary>
    /// Reads a query string. Tolerant: an empty value counts as absent (a GET form sends all its fields) and unknown
    /// parameters are ignored. Never throws on user input: every problem becomes a <see cref="QueryFieldError"/>.
    /// Strategy parameters are only checked for their format here; see <see cref="ValidateParameters"/>.
    /// </summary>
    public static bool TryParse(
        IQueryCollection query,
        [NotNullWhen(true)] out BacktestQuery? result,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reader = new Reader(query);

        var symbol = reader.Text(SymbolKey, "Symbol")?.ToUpperInvariant();
        if (symbol is null)
        {
            reader.ErrorIfNone(SymbolKey, "Symbol is required.");
        }
        else if (!Regex.IsMatch(symbol, Conventions.SymbolPattern, RegexOptions.CultureInvariant))
        {
            reader.Error(SymbolKey, "Symbol must be 1 to 10 characters: letters, digits, '.' or '-'.");
        }

        var timeframe = DefaultTimeframe;
        var timeframeText = reader.Text(TimeframeKey, "Timeframe");
        if (timeframeText is not null && !Timeframes.TryGetValue(timeframeText, out timeframe))
        {
            reader.Error(TimeframeKey, "Timeframe must be 'daily' or 'weekly'.");
        }

        var strategyId = reader.Text(StrategyKey, "Strategy");
        if (strategyId is null)
        {
            reader.ErrorIfNone(StrategyKey, "Strategy is required.");
        }

        var from = reader.Date(FromKey, "Start date");
        var to = reader.Date(ToKey, "End date");
        if (from > to)
        {
            reader.Error(FromKey, "The start date must be on or before the end date.");
        }

        var capital = reader.Number(CapitalKey, "Initial capital") ?? DefaultCapital;
        if (capital <= 0)
        {
            reader.Error(CapitalKey, "Initial capital must be greater than 0.");
        }

        var fee = reader.Number(FeeKey, "Fee") ?? DefaultFeePercent;
        reader.CheckClosedRange(FeeKey, "Fee", fee, MaxFeePercent);

        var slippage = reader.Number(SlippageKey, "Slippage") ?? DefaultSlippagePercent;
        reader.CheckClosedRange(SlippageKey, "Slippage", slippage, MaxSlippagePercent);

        var stopLoss = reader.Number(StopLossKey, "Stop-loss");
        reader.CheckOptionalRate(StopLossKey, "Stop-loss", stopLoss, MaxStopLossPercent);

        var takeProfit = reader.Number(TakeProfitKey, "Take-profit");
        reader.CheckOptionalRate(TakeProfitKey, "Take-profit", takeProfit, MaxTakeProfitPercent);

        var trailingStop = reader.Number(TrailingStopKey, "Trailing stop");
        reader.CheckOptionalRate(TrailingStopKey, "Trailing stop", trailingStop, MaxTrailingStopPercent);

        var riskFree = reader.Number(RiskFreeKey, "Risk-free rate") ?? DefaultRiskFreePercent;
        reader.CheckClosedRange(RiskFreeKey, "Risk-free rate", riskFree, MaxRiskFreePercent);

        var parameters = reader.Parameters();

        errors = reader.Errors;
        if (errors.Count > 0)
        {
            result = null;
            return false;
        }

        result = new BacktestQuery(
            symbol!, timeframe, strategyId!, from, to, capital, fee, slippage, stopLoss, takeProfit, trailingStop, riskFree, parameters);
        return true;
    }

    /// <summary>
    /// Checks the p.* values against the definitions of the selected strategy (from IStrategyCatalog): known key,
    /// value in [Min, Max], whole number for an Integer parameter. Absent keys are fine: the strategy uses its
    /// default (docs/CONTRACT.md section 3). IBacktestService stays the final authority.
    /// </summary>
    public IReadOnlyList<QueryFieldError> ValidateParameters(IReadOnlyList<ParameterDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var errors = new List<QueryFieldError>();
        foreach (var (key, value) in Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var field = ParameterPrefix + key;
            var definition = definitions.FirstOrDefault(d => d.Key == key);
            if (definition is null)
            {
                errors.Add(new QueryFieldError(field, $"Unknown parameter '{key}' for this strategy."));
            }
            else if ((double)value < definition.Min || (double)value > definition.Max)
            {
                errors.Add(new QueryFieldError(
                    field,
                    $"{definition.Label} must be between {FormatBound(definition.Min)} and {FormatBound(definition.Max)}."));
            }
            else if (definition.Type == ParameterType.Integer && value != decimal.Truncate(value))
            {
                errors.Add(new QueryFieldError(field, $"{definition.Label} must be a whole number."));
            }
        }

        return errors;
    }

    /// <summary>
    /// The same query with every parameter of the strategy made explicit: received values are kept, missing keys
    /// get the catalog default. The permalink then pins the parameters, even if a default changes later.
    /// Call after <see cref="ValidateParameters"/> succeeded.
    /// </summary>
    public BacktestQuery WithDefaultParameters(IReadOnlyList<ParameterDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var parameters = new Dictionary<string, decimal>(Parameters, StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            parameters.TryAdd(definition.Key, ToDecimal(definition.DefaultValue));
        }

        return this with { Parameters = parameters };
    }

    /// <summary>
    /// True when the received query string is exactly <see cref="ToQueryString"/> once both are decoded: same
    /// names, same values, same order. Comparing decoded pairs (not raw text) means "%2E" and "." are equal, so a
    /// redirect to the canonical URL can never loop on encoding differences.
    /// </summary>
    public bool IsCanonical(QueryString received) =>
        DecodePairs(received.Value).SequenceEqual(DecodePairs(ToQueryString()));

    /// <summary>
    /// Canonical query string, without the leading "?": symbol, tf, strategy, from, to, capital, fee, slip, sl, tp,
    /// ts, rf, then p.* sorted by key (ordinal). Optional fields are written only when set; numbers in their shortest
    /// invariant form. Two queries with the same settings always give the same string.
    /// </summary>
    public string ToQueryString()
    {
        var builder = new StringBuilder();
        Append(builder, SymbolKey, Symbol);
        Append(builder, TimeframeKey, FormatTimeframe(Timeframe));
        Append(builder, StrategyKey, StrategyId);
        Append(builder, FromKey, From?.ToString(DateFormat, CultureInfo.InvariantCulture));
        Append(builder, ToKey, To?.ToString(DateFormat, CultureInfo.InvariantCulture));
        Append(builder, CapitalKey, FormatNumber(Capital));
        Append(builder, FeeKey, FormatNumber(FeePercent));
        Append(builder, SlippageKey, FormatNumber(SlippagePercent));
        Append(builder, StopLossKey, FormatNumber(StopLossPercent));
        Append(builder, TakeProfitKey, FormatNumber(TakeProfitPercent));
        Append(builder, TrailingStopKey, FormatNumber(TrailingStopPercent));
        Append(builder, RiskFreeKey, FormatNumber(RiskFreePercent));
        foreach (var (key, value) in Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Append(builder, ParameterPrefix + key, FormatNumber(value));
        }

        return builder.ToString();
    }

    /// <summary>The request of the contract: percent → fraction (value / 100), p.* passed as received.</summary>
    public BacktestRequest ToBacktestRequest() => new(
        Symbol,
        Timeframe,
        StrategyId,
        Parameters.ToDictionary(p => p.Key, p => (double)p.Value, StringComparer.Ordinal),
        From,
        To,
        new ExecutionSettings(
            Capital,
            FeePercent / 100m,
            SlippagePercent / 100m,
            StopLossPercent / 100m,
            TakeProfitPercent / 100m,
            TrailingStopPercent / 100m,
            (double)(RiskFreePercent / 100m)));

    /// <summary>URL spelling of a timeframe ("daily", "weekly"), also used by /api/strategies.</summary>
    public static string FormatTimeframe(Timeframe timeframe) => timeframe switch
    {
        Timeframe.Daily => "daily",
        Timeframe.Weekly => "weekly",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };

    /// <summary>Shortest invariant form of a number, never an exponent: 10000.00 → "10000", 0.00001 → "0.00001".</summary>
    public static string? FormatNumber(decimal? value)
    {
        if (value is not { } number)
        {
            return null;
        }

        // "-0" parses to a negative zero, which must print as "0".
        return (number == 0 ? 0m : number).ToString(ShortestDecimal, CultureInfo.InvariantCulture);
    }

    /// <summary>A catalog value (double) in the same notation: 1E-05 → "0.00001", 1E+07 → "10000000".</summary>
    public static string FormatNumber(double value) => FormatNumber(ToDecimal(value))!;

    // Catalog values are small, finite and have few significant digits: the decimal conversion is exact for them.
    private static decimal ToDecimal(double value) => (decimal)value;

    private static IEnumerable<(string Name, string Value)> DecodePairs(string? queryString)
    {
        foreach (var pair in new QueryStringEnumerable(queryString))
        {
            yield return (pair.DecodeName().ToString(), pair.DecodeValue().ToString());
        }
    }

    private static string FormatPercent(decimal percent) => FormatNumber(percent) + "%";

    private static string FormatBound(double bound) => bound.ToString("0.##########", CultureInfo.InvariantCulture);

    private static void Append(StringBuilder builder, string key, string? value)
    {
        if (value is null)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('&');
        }

        builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
    }

    /// <summary>Reads raw values and collects errors in field order, so one bad field never hides another.</summary>
    private sealed class Reader(IQueryCollection query)
    {
        private readonly List<QueryFieldError> _errors = [];

        public IReadOnlyList<QueryFieldError> Errors => _errors;

        public void Error(string field, string message) => _errors.Add(new QueryFieldError(field, message));

        /// <summary>Adds the error unless the field already has one (a repeated symbol is not also "required").</summary>
        public void ErrorIfNone(string field, string message)
        {
            if (!_errors.Exists(e => e.Field == field))
            {
                Error(field, message);
            }
        }

        /// <summary>Trimmed value; null when absent or blank. A repeated parameter is an error.</summary>
        public string? Text(string key, string label)
        {
            var values = query[key];
            if (values.Count > 1)
            {
                Error(key, $"{label} is given more than once.");
                return null;
            }

            var text = values.ToString().Trim();
            return text.Length == 0 ? null : text;
        }

        public decimal? Number(string key, string label)
        {
            var text = Text(key, label);
            if (text is null)
            {
                return null;
            }

            if (decimal.TryParse(text, NumberStyle, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            Error(key, $"{label} must be a number, with '.' as the decimal separator.");
            return null;
        }

        public DateOnly? Date(string key, string label)
        {
            var text = Text(key, label);
            if (text is null)
            {
                return null;
            }

            if (DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            Error(key, $"{label} must be a date in the {DateFormat} format.");
            return null;
        }

        /// <summary>Value in [0, max] (fee, slippage, risk-free rate).</summary>
        public void CheckClosedRange(string key, string label, decimal value, decimal max)
        {
            if (value < 0 || value > max)
            {
                Error(key, $"{label} must be between 0% and {FormatPercent(max)}.");
            }
        }

        /// <summary>Optional protective exit in ]0, max]; absent means disabled.</summary>
        public void CheckOptionalRate(string key, string label, decimal? value, decimal max)
        {
            if (value is <= 0 || value > max)
            {
                Error(key, $"{label} must be greater than 0% and at most {FormatPercent(max)} (leave empty to disable).");
            }
        }

        /// <summary>Every non-blank p.&lt;key&gt; received, format checked only (the catalog is not known here).</summary>
        public IReadOnlyDictionary<string, decimal> Parameters()
        {
            var parameters = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var keys = query.Keys
                .Where(k => k.StartsWith(ParameterPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal);
            foreach (var urlKey in keys)
            {
                var key = urlKey[ParameterPrefix.Length..];
                var label = $"Parameter '{key}'";
                if (key.Length == 0)
                {
                    if (Text(urlKey, "Parameter") is not null)
                    {
                        Error(urlKey, $"A parameter name is missing after '{ParameterPrefix}'.");
                    }

                    continue;
                }

                if (Number(urlKey, label) is { } value)
                {
                    parameters[key] = value;
                }
            }

            return parameters;
        }
    }
}
