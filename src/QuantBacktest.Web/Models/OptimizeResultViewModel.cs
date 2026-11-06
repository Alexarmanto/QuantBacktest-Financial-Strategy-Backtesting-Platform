using System.Globalization;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The heatmap result page (P4-X2): what was swept, with which settings, the permalink, the link back to the prefilled
/// form, the heatmap itself, and the link of its optimistic note to the train/test form prefilled with the same grid
/// (P4-X2 "validate it on unseen data (Validate page)", P4-X3). The period is the requested one: SweepResult carries no bars (decision g-12).
/// </summary>
internal sealed record OptimizeResultViewModel(
    string Symbol,
    string StrategyName,
    string Timeframe,
    string Period,
    string MetricLabel,
    IReadOnlyList<SettingRow> Axes,
    IReadOnlyList<SettingRow> FixedParameters,
    IReadOnlyList<SettingRow> Execution,
    string Permalink,
    string EditSettingsUrl,
    HeatmapViewModel Heatmap,
    string ValidateUrl)
{
    public const string FormPath = "/Optimize";
    public const string ResultPath = "/Optimize/Result";
    public const string WholePeriod = "All available bars";

    /// <param name="query">The resolved, canonical query of this page.</param>
    /// <param name="permalink">Absolute URL of this page, copied by "Copy link".</param>
    public static OptimizeResultViewModel Create(
        SweepResult result,
        IStrategy strategy,
        OptimizationQuery query,
        string permalink,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(permalink);
        ArgumentNullException.ThrowIfNull(pathBase);

        var settings = query.Settings;
        var fixedParameters = strategy.Parameters
            .Where(d => settings.Parameters.ContainsKey(d.Key))
            .Select(d => new SettingRow(d.Label, BacktestQuery.FormatNumber(settings.Parameters[d.Key])!))
            .ToList();

        return new OptimizeResultViewModel(
            query.Symbol,
            strategy.Name,
            query.Timeframe.ToString(),
            FormatPeriod(settings.From, settings.To),
            SweepMetrics.Label(query.Metric),
            [Axis("X", strategy, query.X), Axis("Y", strategy, query.Y)],
            fixedParameters,
            ResultHeaderViewModel.ExecutionRows(settings.ToBacktestRequest().Execution),
            permalink,
            pathBase + FormPath + "?" + query.ToQueryString(),
            HeatmapViewModel.Create(result, strategy, query, pathBase),
            pathBase + ValidateResultViewModel.FormPath + "?"
                + settings.ToValidationQueryString(query.X, query.Y, query.Metric, BacktestQuery.DefaultTrainPercent));
    }

    internal static SettingRow Axis(string name, IStrategy strategy, SweepAxisInput axis)
    {
        var label = strategy.Parameters.FirstOrDefault(d => d.Key == axis.Key)?.Label ?? axis.Key!;
        return new SettingRow(
            $"{name}: {label}",
            $"{BacktestQuery.FormatNumber(axis.Min)} to {BacktestQuery.FormatNumber(axis.Max)}, step {BacktestQuery.FormatNumber(axis.Step)}");
    }

    internal static string FormatPeriod(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        (null, null) => WholePeriod,
        ({ } start, null) => $"From {Format(start)}",
        (null, { } end) => $"Up to {Format(end)}",
        ({ } start, { } end) => $"{Format(start)} to {Format(end)}",
    };

    private static string Format(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
}
