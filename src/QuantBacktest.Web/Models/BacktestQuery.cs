using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
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

    // Comparator (P4-X1, C1): strategies=id1,id2,… replaces strategy; ids keep the order of the URL.
    public const string StrategiesKey = "strategies";
    public const char StrategiesSeparator = ',';

    // C1: "de 2 à Conventions.MaxComparedStrategies stratégies". The lower bound 2 is only written in docs/CONTRACT.md
    // (C1), not in Conventions: it is spelled once here.
    public const int MinComparedStrategies = 2;
    public const int MaxComparedStrategies = Conventions.MaxComparedStrategies;

    public static readonly string TooFewStrategiesMessage =
        $"Select at least {FormatInteger(MinComparedStrategies)} strategies to compare.";

    public static readonly string TooManyStrategiesMessage =
        $"Select at most {FormatInteger(MaxComparedStrategies)} strategies to compare.";

    // Monte Carlo (P4-X4). sims and seed belong to the Monte Carlo page and to the PDF; mc=true only to the PDF
    // URL (PART-4 section 5, "Report.pdf?… &mc=true … with sims and seed").
    public const string SimulationsKey = "sims";
    public const string SeedKey = "seed";
    public const string MonteCarloKey = "mc";
    public const string MonteCarloFlag = "true";

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

    // MC1: Simulations in [100, MaxMonteCarloSimulations]. The lower bound 100 is only written in docs/CONTRACT.md
    // (MC1), not in Conventions: it is spelled once here. The seed has no range in the contract: any Int32.
    public const int MinMonteCarloSimulations = 100;
    public const int MaxMonteCarloSimulations = Conventions.MaxMonteCarloSimulations;
    public const int DefaultMonteCarloSimulations = Conventions.DefaultMonteCarloSimulations;
    public const int DefaultMonteCarloSeed = Conventions.DefaultMonteCarloSeed;

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

        var parsed = ReadSettings(
            reader,
            r =>
            {
                var strategyId = r.Text(StrategyKey, "Strategy");
                if (strategyId is null)
                {
                    r.ErrorIfNone(StrategyKey, "Strategy is required.");
                }

                return strategyId;
            },
            readParameters: true);

        errors = reader.Errors;
        if (errors.Count > 0)
        {
            result = null;
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>
    /// Reads the URL of the comparator (P4-X1, C1): the settings of <see cref="TryParse"/>, read by the same code, with
    /// strategies=id1,id2,… in place of strategy. strategies may also be repeated (a GET form sends one pair per checked
    /// box): the values are merged in the order received, split on ',', trimmed; empty items are ignored. A duplicate,
    /// fewer than <see cref="MinComparedStrategies"/> or more than Conventions.MaxComparedStrategies ids are field
    /// errors. strategy, p.*, sims, seed, mc and unknown parameters are ignored: each strategy runs with its defaults.
    /// Whether an id exists is not checked here (the catalog is not known). Never throws on user input.
    /// </summary>
    public static bool TryParseComparison(
        IQueryCollection query,
        [NotNullWhen(true)] out ComparisonQuery? result,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reader = new Reader(query);

        IReadOnlyList<string> strategyIds = [];
        var settings = ReadSettings(
            reader,
            r =>
            {
                strategyIds = ReadStrategyIds(query);
                var duplicates = strategyIds
                    .GroupBy(id => id, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToList();
                if (duplicates.Count > 0)
                {
                    foreach (var duplicate in duplicates)
                    {
                        r.Error(StrategiesKey, $"Strategy '{duplicate}' is selected more than once.");
                    }
                }
                else if (strategyIds.Count < MinComparedStrategies)
                {
                    r.Error(StrategiesKey, TooFewStrategiesMessage);
                }
                else if (strategyIds.Count > MaxComparedStrategies)
                {
                    r.Error(StrategiesKey, TooManyStrategiesMessage);
                }

                // The settings carry no strategy: ComparisonQuery.ForStrategy sets it, with its default parameters.
                return string.Empty;
            },
            readParameters: false);

        errors = reader.Errors;
        if (errors.Count > 0)
        {
            result = null;
            return false;
        }

        result = new ComparisonQuery(settings, strategyIds);
        return true;
    }

    /// <summary>
    /// The ids of strategies= as received (repeated keys merged, split on ',', trimmed, empty items dropped), without
    /// any check: the comparator form uses them to keep its boxes checked (P4-U12).
    /// </summary>
    public static IReadOnlyList<string> ReadStrategyIds(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query[StrategiesKey]
            .SelectMany(value => (value ?? string.Empty).Split(StrategiesSeparator))
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .ToList();
    }

    /// <summary>A URL spelling of tf ("daily", "weekly", any case); false for anything else.</summary>
    public static bool TryReadTimeframe(string? text, out Timeframe timeframe)
    {
        timeframe = DefaultTimeframe;
        return text is not null && Timeframes.TryGetValue(text.Trim(), out timeframe);
    }

    /// <summary>
    /// The settings shared by every URL of a backtest, in field order; the strategy slot is read by readStrategy.
    /// The returned value is meaningful only when the reader collected no error.
    /// </summary>
    private static BacktestQuery ReadSettings(Reader reader, Func<Reader, string?> readStrategy, bool readParameters)
    {
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

        var strategyId = readStrategy(reader);

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

        IReadOnlyDictionary<string, decimal> parameters = readParameters
            ? reader.Parameters()
            : new Dictionary<string, decimal>(StringComparer.Ordinal);

        return new BacktestQuery(
            symbol ?? string.Empty,
            timeframe,
            strategyId ?? string.Empty,
            from,
            to,
            capital,
            fee,
            slippage,
            stopLoss,
            takeProfit,
            trailingStop,
            riskFree,
            parameters);
    }

    /// <summary>
    /// Reads sims and seed of the Monte Carlo page (P4-X4). Same tolerance as <see cref="TryParse"/>: an empty or
    /// absent value takes the default of Conventions; every other parameter (mc included) is ignored here.
    /// Never throws on user input. MC1 range for sims; the seed is any whole Int32 (no range in the contract).
    /// </summary>
    public static bool TryParseMonteCarlo(
        IQueryCollection query,
        [NotNullWhen(true)] out MonteCarloRequest? result,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reader = new Reader(query);

        var simulationsMessage =
            $"Simulations must be a whole number between {FormatInteger(MinMonteCarloSimulations)} and {FormatInteger(MaxMonteCarloSimulations)}.";
        var simulations = reader.Integer(SimulationsKey, "Simulations", simulationsMessage) ?? DefaultMonteCarloSimulations;
        if (simulations is < MinMonteCarloSimulations or > MaxMonteCarloSimulations)
        {
            reader.Error(SimulationsKey, simulationsMessage);
        }

        var seed = reader.Integer(
            SeedKey,
            "Seed",
            $"Seed must be a whole number between {FormatInteger(int.MinValue)} and {FormatInteger(int.MaxValue)}.")
            ?? DefaultMonteCarloSeed;

        errors = reader.Errors;
        if (errors.Count > 0)
        {
            result = null;
            return false;
        }

        result = new MonteCarloRequest(simulations, seed);
        return true;
    }

    /// <summary>Error of an mc value other than "true" (decision D1). mc has no input on the form: shown at the top.</summary>
    public const string MonteCarloFlagMessage = "mc must be 'true' to add the Monte Carlo section, or be left out.";

    /// <summary>
    /// Reads mc of the PDF URL (PART-4 section 5, decision D1): absent or empty → false; "true" (any case) → true;
    /// any other value, or mc given more than once, is a field error. sims and seed are read by
    /// <see cref="TryParseMonteCarlo"/>, only when mc is true. Never throws on user input.
    /// </summary>
    public static bool TryParseReportFlag(
        IQueryCollection query,
        out bool withMonteCarlo,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reader = new Reader(query);

        withMonteCarlo = false;
        var text = reader.Text(MonteCarloKey, "Monte Carlo flag (mc)");
        if (text is not null)
        {
            if (string.Equals(text, MonteCarloFlag, StringComparison.OrdinalIgnoreCase))
            {
                withMonteCarlo = true;
            }
            else
            {
                reader.Error(MonteCarloKey, MonteCarloFlagMessage);
            }
        }

        errors = reader.Errors;
        return errors.Count == 0;
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
    /// Monte Carlo page (P4-X4), or the PDF with its Monte Carlo section (forReport): true when the received query
    /// string is exactly <see cref="ToQueryString(MonteCarloRequest, bool)"/> with the same forReport, once decoded.
    /// For the page, a received mc is not canonical; for the PDF, mc=true must come before sims and seed. An absent
    /// sims or seed, or another order, is not canonical either, so the action redirects once.
    /// </summary>
    public bool IsCanonical(QueryString received, MonteCarloRequest monteCarlo, bool forReport = false) =>
        DecodePairs(received.Value).SequenceEqual(DecodePairs(ToQueryString(monteCarlo, forReport)));

    /// <summary>
    /// Canonical query string, without the leading "?": symbol, tf, strategy, from, to, capital, fee, slip, sl, tp,
    /// ts, rf, then p.* sorted by key (ordinal). Optional fields are written only when set; numbers in their shortest
    /// invariant form. Two queries with the same settings always give the same string.
    /// </summary>
    public string ToQueryString() => Encode(ToQueryPairs());

    /// <summary>
    /// The canonical query string of <see cref="ToQueryString()"/> followed by the Monte Carlo settings: sims, seed for
    /// the Monte Carlo page; mc=true, sims, seed for the PDF with its Monte Carlo section (forReport, PART-4 section 5).
    /// </summary>
    public string ToQueryString(MonteCarloRequest monteCarlo, bool forReport = false)
    {
        ArgumentNullException.ThrowIfNull(monteCarlo);
        var pairs = ToQueryPairs().ToList();
        if (forReport)
        {
            pairs.Add(new(MonteCarloKey, MonteCarloFlag));
        }

        pairs.Add(new(SimulationsKey, FormatInteger(monteCarlo.Simulations)));
        pairs.Add(new(SeedKey, FormatInteger(monteCarlo.Seed)));
        return Encode(pairs);
    }

    /// <summary>
    /// The decoded name / value pairs of <see cref="ToQueryString()"/>, in the same canonical order. A GET form that
    /// must keep these settings (Monte Carlo form) writes them as hidden fields, in this order, before its own fields.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ToQueryPairs() =>
        SettingsPairs(new(StrategyKey, StrategyId), withParameters: true);

    /// <summary>
    /// Canonical query string of the comparator (P4-X1), without the leading "?": symbol, tf, strategies, from, to,
    /// capital, fee, slip, sl, tp, ts, rf. The ids keep their order and are joined by a literal ',' (each id escaped on
    /// its own), so the permalink stays readable. No strategy and no p.*: every strategy runs with its defaults.
    /// </summary>
    public string ToComparisonQueryString(IReadOnlyList<string> strategyIds)
    {
        ArgumentNullException.ThrowIfNull(strategyIds);
        var joined = string.Join(StrategiesSeparator, strategyIds);
        var builder = new StringBuilder();
        foreach (var (key, value) in SettingsPairs(new(StrategiesKey, joined), withParameters: false))
        {
            if (key == StrategiesKey)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                builder.Append(StrategiesKey).Append('=')
                    .Append(string.Join(StrategiesSeparator, strategyIds.Select(Uri.EscapeDataString)));
            }
            else
            {
                Append(builder, key, value);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when the received query string is exactly <see cref="ToComparisonQueryString"/> once both are decoded, so
    /// "%2C" and "," are equal and the redirect to the canonical URL can never loop.
    /// </summary>
    public bool IsCanonicalComparison(QueryString received, IReadOnlyList<string> strategyIds) =>
        DecodePairs(received.Value).SequenceEqual(DecodePairs(ToComparisonQueryString(strategyIds)));

    private List<KeyValuePair<string, string>> SettingsPairs(KeyValuePair<string, string> strategyPair, bool withParameters)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        void Add(string key, string? value)
        {
            if (value is not null)
            {
                pairs.Add(new(key, value));
            }
        }

        Add(SymbolKey, Symbol);
        Add(TimeframeKey, FormatTimeframe(Timeframe));
        Add(strategyPair.Key, strategyPair.Value);
        Add(FromKey, From?.ToString(DateFormat, CultureInfo.InvariantCulture));
        Add(ToKey, To?.ToString(DateFormat, CultureInfo.InvariantCulture));
        Add(CapitalKey, FormatNumber(Capital));
        Add(FeeKey, FormatNumber(FeePercent));
        Add(SlippageKey, FormatNumber(SlippagePercent));
        Add(StopLossKey, FormatNumber(StopLossPercent));
        Add(TakeProfitKey, FormatNumber(TakeProfitPercent));
        Add(TrailingStopKey, FormatNumber(TrailingStopPercent));
        Add(RiskFreeKey, FormatNumber(RiskFreePercent));
        if (withParameters)
        {
            foreach (var (key, value) in Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Add(ParameterPrefix + key, FormatNumber(value));
            }
        }

        return pairs;
    }

    /// <summary>Invariant whole number, no thousands separator: 10000 → "10000", -42 → "-42".</summary>
    public static string FormatInteger(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Query string, without the leading "?", of a backtest form that preselects one strategy ("Use this strategy",
    /// P4-U8): strategy=&lt;id&gt;, plus tf=&lt;first supported timeframe&gt; when the strategy does not support the
    /// default timeframe of the form. Every other field keeps its form default.
    /// </summary>
    public static string FormQueryString(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        var builder = new StringBuilder();
        Append(builder, StrategyKey, strategy.Id);
        if (!strategy.SupportedTimeframes.Contains(DefaultTimeframe) && strategy.SupportedTimeframes.Count > 0)
        {
            Append(builder, TimeframeKey, FormatTimeframe(strategy.SupportedTimeframes.Order().First()));
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

    private static string Encode(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var builder = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            Append(builder, key, value);
        }

        return builder.ToString();
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

        /// <summary>Whole Int32 in plain notation: "1000", "-42". "1.5", "1e3", "1,000" or an Int32 overflow add invalidMessage.</summary>
        public int? Integer(string key, string label, string invalidMessage)
        {
            var text = Text(key, label);
            if (text is null)
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            Error(key, invalidMessage);
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
