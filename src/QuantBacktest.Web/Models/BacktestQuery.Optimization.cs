using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using QuantBacktest.Core;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One axis of a heatmap as read from the URL (P4-X2): the parameter key and its range, in URL units. Each value is
/// null when the URL does not carry it; <see cref="OptimizationQuery.Resolve"/> fills it from the definition.
/// </summary>
internal sealed record SweepAxisInput(string? Key, decimal? Min, decimal? Max, decimal? Step)
{
    public bool IsComplete => Key is not null && Min is not null && Max is not null && Step is not null;
}

/// <summary>
/// The optimisation heatmap part of the permalink (P4-X2, docs/CONTRACT.md section 6): the URL names x, xmin, xmax,
/// xstep, y, ymin, ymax, ystep and metric, read by the same Reader as every other setting and written in canonical order.
/// </summary>
internal sealed partial record BacktestQuery
{
    // P4-X2: "URL : réglages du backtest + x, xmin, xmax, xstep, y, ymin, ymax, ystep, metric".
    public const string XKey = "x";
    public const string XMinKey = "xmin";
    public const string XMaxKey = "xmax";
    public const string XStepKey = "xstep";
    public const string YKey = "y";
    public const string YMinKey = "ymin";
    public const string YMaxKey = "ymax";
    public const string YStepKey = "ystep";
    public const string MetricKey = "metric";

    /// <summary>Heatmap URL names, in canonical order (after the backtest settings).</summary>
    public static IReadOnlyList<string> SweepKeys { get; } =
        [XKey, XMinKey, XMaxKey, XStepKey, YKey, YMinKey, YMaxKey, YStepKey, MetricKey];

    /// <summary>PART-4 does not name a default metric: Sharpe, the first value of SweepMetric.</summary>
    public const SweepMetric DefaultMetric = SweepMetric.SharpeRatio;

    public const int MaxSweepCells = Conventions.MaxSweepCells;

    /// <summary>P4-X2: the default step gives at most 25 values, so 24 intervals between Min and Max.</summary>
    public const int DefaultAxisIntervals = 24;

    /// <summary>The epsilon of O1 ("tant que &lt;= Max + 1e-9") and of ParameterSweepService.BuildAxis.</summary>
    public const double AxisEpsilon = 1e-9;

    /// <summary>URL spelling of each metric, in the order of SweepMetric. An explicit table: "0" is never read as an enum value.</summary>
    private static readonly IReadOnlyList<(string Token, SweepMetric Metric)> MetricTokens =
    [
        ("sharpe", SweepMetric.SharpeRatio),
        ("sortino", SweepMetric.SortinoRatio),
        ("calmar", SweepMetric.CalmarRatio),
        ("cumulative-return", SweepMetric.CumulativeReturn),
        ("annualized-return", SweepMetric.AnnualizedReturn),
        ("profit-factor", SweepMetric.ProfitFactor),
    ];

    public static readonly string UnknownMetricMessage =
        $"Metric must be one of: {string.Join(", ", MetricTokens.Select(m => m.Token))}.";

    /// <summary>Every metric of the contract, in display order.</summary>
    public static IReadOnlyList<SweepMetric> SweepMetricsInOrder { get; } = MetricTokens.Select(m => m.Metric).ToList();

    /// <summary>URL spelling of a metric ("sharpe", "profit-factor").</summary>
    public static string FormatMetric(SweepMetric metric)
    {
        foreach (var (token, value) in MetricTokens)
        {
            if (value == metric)
            {
                return token;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown metric.");
    }

    /// <summary>A URL spelling of metric (any case); false for anything else.</summary>
    public static bool TryReadMetric(string? text, out SweepMetric metric)
    {
        metric = DefaultMetric;
        if (text is null)
        {
            return false;
        }

        foreach (var (token, value) in MetricTokens)
        {
            if (string.Equals(token, text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                metric = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the URL of the heatmap (P4-X2): the settings of <see cref="TryParse"/>, read by the same code (strategy and
    /// p.* included), then the two axes and the metric. Only the format is checked here; the axes are checked against
    /// the strategy by <see cref="OptimizationQuery.Resolve"/>. An absent value takes its default there. Never throws.
    /// </summary>
    public static bool TryParseOptimization(
        IQueryCollection query,
        [NotNullWhen(true)] out OptimizationQuery? result,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reader = new Reader(query);
        var optimization = ReadOptimization(reader);

        errors = reader.Errors;
        if (errors.Count > 0)
        {
            result = null;
            return false;
        }

        result = optimization;
        return true;
    }

    /// <summary>
    /// The heatmap part of a URL, read once for the heatmap (TryParseOptimization) and the train/test page
    /// (TryParseValidation): settings, strategy, p.*, both axes and the metric. Errors go to the reader.
    /// </summary>
    private static OptimizationQuery ReadOptimization(Reader reader)
    {
        var settings = ReadSettings(
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

        var x = ReadAxis(reader, "X", XKey, XMinKey, XMaxKey, XStepKey);
        var y = ReadAxis(reader, "Y", YKey, YMinKey, YMaxKey, YStepKey);

        var metric = DefaultMetric;
        var metricText = reader.Text(MetricKey, "Metric");
        if (metricText is not null && !TryReadMetric(metricText, out metric))
        {
            reader.Error(MetricKey, UnknownMetricMessage);
        }

        return new OptimizationQuery(settings, x, y, metric);
    }

    /// <summary>
    /// P4-X2 default step: Step × ceil((Max - Min) / (24 × Step)), so at most 25 values. Computed in decimal (catalog
    /// values convert exactly), so ceil never sees a binary rounding error. A single-value range keeps the catalog Step.
    /// </summary>
    public static decimal DefaultSweepStep(ParameterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var min = ToDecimal(definition.Min);
        var max = ToDecimal(definition.Max);
        var step = ToDecimal(definition.Step);
        var intervals = Math.Max(1m, Math.Ceiling((max - min) / (DefaultAxisIntervals * step)));
        return step * intervals;
    }

    /// <summary>
    /// Number of values of an axis, exactly as Engine/ParameterSweepService.cs:162 counts them (O1):
    /// Math.Floor(((Max - Min) / Step) + 1e-9) + 1, in double, on the same doubles that go into SweepAxis.
    /// Counting the cells of a grid is not financial logic.
    /// </summary>
    public static double AxisValueCount(decimal min, decimal max, decimal step) =>
        Math.Floor((((double)max - (double)min) / (double)step) + AxisEpsilon) + 1;

    /// <summary>A catalog or axis value (double) as a parameter value of a backtest URL: 0.07500000000000001 → 0.075.</summary>
    public static decimal ParameterValue(double value) => ToDecimal(value);

    /// <summary>
    /// Canonical query string of the heatmap, without the leading "?": the settings of <see cref="ToQueryString()"/>
    /// (symbol … rf, then p.* sorted by key), then x, xmin, xmax, xstep, y, ymin, ymax, ystep, metric.
    /// </summary>
    public string ToOptimizationQueryString(SweepAxisInput x, SweepAxisInput y, SweepMetric metric) =>
        Encode(OptimizationPairs(x, y, metric));

    /// <summary>True when the received query string is exactly <see cref="ToOptimizationQueryString"/> once both are decoded.</summary>
    public bool IsCanonicalOptimization(QueryString received, SweepAxisInput x, SweepAxisInput y, SweepMetric metric) =>
        DecodePairs(received.Value).SequenceEqual(DecodePairs(ToOptimizationQueryString(x, y, metric)));

    // Shared with ToValidationQueryString (BacktestQuery.Validation.cs), which appends train.
    private List<KeyValuePair<string, string>> OptimizationPairs(SweepAxisInput x, SweepAxisInput y, SweepMetric metric)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        var pairs = ToQueryPairs().ToList();
        void Add(string key, string? value)
        {
            if (value is not null)
            {
                pairs.Add(new(key, value));
            }
        }

        Add(XKey, x.Key);
        Add(XMinKey, FormatNumber(x.Min));
        Add(XMaxKey, FormatNumber(x.Max));
        Add(XStepKey, FormatNumber(x.Step));
        Add(YKey, y.Key);
        Add(YMinKey, FormatNumber(y.Min));
        Add(YMaxKey, FormatNumber(y.Max));
        Add(YStepKey, FormatNumber(y.Step));
        Add(MetricKey, FormatMetric(metric));
        return pairs;
    }

    private static SweepAxisInput ReadAxis(Reader reader, string axis, string key, string minKey, string maxKey, string stepKey) =>
        new(
            reader.Text(key, $"{axis} parameter"),
            reader.Number(minKey, $"{axis} minimum"),
            reader.Number(maxKey, $"{axis} maximum"),
            reader.Number(stepKey, $"{axis} step"));
}
