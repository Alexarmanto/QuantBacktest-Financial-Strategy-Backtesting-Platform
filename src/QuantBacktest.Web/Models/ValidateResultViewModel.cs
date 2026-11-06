using System.Globalization;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One line of the train vs test table (P4-X3, T3: "côte à côte, sans autre calcul"): the same field of the two
/// results, formatted by MetricDefinitions. IsOptimized marks the metric of the heatmap.
/// </summary>
internal sealed record TrainTestMetricRow(
    string Label,
    string Train,
    string Test,
    bool IsOptimized,
    string TrainTone = ValueTone.Neutral,
    string TestTone = ValueTone.Neutral);

/// <summary>The best cell of the training heatmap (SweepResult.Best, O3) as shown; FewTradesNote only below 5 trades.</summary>
internal sealed record BestCellView(IReadOnlyList<SettingRow> Parameters, string ValueText, string TradesText, string? FewTradesNote);

/// <summary>
/// The train/test page (P4-X3, T1 to T3): cut dates, best parameters, train vs test table, the two equity curves, the
/// training heatmap and the T2 note. Every value comes from the services; this model formats and builds URLs only.
/// Without a best cell, Best, Metrics, Reading, the URLs of the test and the curves are absent (nothing was tested).
/// </summary>
internal sealed record ValidateResultViewModel(
    string Symbol,
    string StrategyName,
    string Timeframe,
    string Period,
    string MetricLabel,
    string TrainShare,
    IReadOnlyList<SettingRow> Split,
    IReadOnlyList<SettingRow> Grid,
    IReadOnlyList<SettingRow> Execution,
    string Permalink,
    string EditSettingsUrl,
    string FullHeatmapUrl,
    BestCellView? Best,
    IReadOnlyList<TrainTestMetricRow> Metrics,
    string? Reading,
    string? CapitalNote,
    bool TestHasNoTrades,
    string? TestResultUrl,
    HeatmapViewModel Heatmap,
    ValidateChartsViewModel Charts)
{
    public const string FormPath = "/Validate";
    public const string ResultPath = "/Validate/Result";

    public const string TrainColumn = "Train";
    public const string TestColumn = "Test";

    // T2, in English for the page.
    public const string WarmUpNote =
        "The test backtest may use the bars before its first date to warm up its indicators (rule T2). "
        + "This is not a leak of information: every signal still depends only on past bars.";

    // P4-X3: "Aucune meilleure cellule → message clair".
    public const string NoBestMessage =
        "No combination produced a value on the training period, so there are no best parameters to test.";

    public const string OptimizedNote = "Optimized metric.";

    /// <param name="sweep">The answer of IParameterSweepService for the training grid.</param>
    /// <param name="train">The backtest of the best cell on the training period; null without a best cell.</param>
    /// <param name="test">The backtest of the best cell on the test period; null without a best cell.</param>
    /// <param name="query">The resolved, canonical query of this page.</param>
    public static ValidateResultViewModel Create(
        SweepResult sweep,
        BacktestResult? train,
        BacktestResult? test,
        IStrategy strategy,
        ValidationQuery query,
        TrainTestSplit split,
        string permalink,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(permalink);
        ArgumentNullException.ThrowIfNull(pathBase);

        var settings = query.Settings;
        var metric = query.Metric;
        var metricLabel = SweepMetrics.Label(metric);
        var trainGrid = query.TrainGrid(split);
        var heatmap = HeatmapViewModel.Create(sweep, strategy, trainGrid, pathBase, linkCells: false);
        var tested = sweep.Best is not null && train is not null && test is not null;

        var grid = new List<SettingRow>
        {
            OptimizeResultViewModel.Axis("X", strategy, query.Optimization.X),
            OptimizeResultViewModel.Axis("Y", strategy, query.Optimization.Y),
        };
        grid.AddRange(strategy.Parameters
            .Where(d => settings.Parameters.ContainsKey(d.Key))
            .Select(d => new SettingRow(d.Label, BacktestQuery.FormatNumber(settings.Parameters[d.Key])!)));

        var splitRows = new List<SettingRow>
        {
            new("Bars in the period", Count(split.BarCount)),
            new("Training period", $"{FormatDate(split.TrainFrom)} to {FormatDate(split.TrainTo)} ({Count(split.TrainCount)} bars)"),
            new("Test period", $"{FormatDate(split.TestFrom)} to {FormatDate(split.TestTo)} ({Count(split.TestCount)} bars)"),
        };

        BestCellView? best = null;
        IReadOnlyList<TrainTestMetricRow> rows = [];
        string? reading = null;
        string? capitalNote = null;
        string? testUrl = null;
        if (tested)
        {
            var cell = sweep.Best!;
            best = new BestCellView(
                [
                    new(ParameterLabel(strategy, query.Optimization.X.Key!), BacktestQuery.FormatNumber(cell.X)),
                    new(ParameterLabel(strategy, query.Optimization.Y.Key!), BacktestQuery.FormatNumber(cell.Y)),
                ],
                SweepMetrics.Format(metric, cell.Value),
                Count(cell.TradeCount),
                cell.TradeCount < HeatmapViewModel.FewTradesThreshold ? HeatmapViewModel.FewTradesNote : null);

            rows = MetricDefinitions.For(query.Timeframe)
                .Select(d => new TrainTestMetricRow(
                    d.Label, d.Format(train!.Metrics), d.Format(test!.Metrics), d.Sweep == metric, d.Tone(train.Metrics), d.Tone(test.Metrics)))
                .ToList();

            reading = ReadingSentence(metric, train!.Metrics, test!.Metrics);
            capitalNote = $"Each period starts with the same initial capital, {DisplayFormat.Money(settings.ToBacktestRequest().Execution.InitialCapital)}.";
            testUrl = pathBase + ResultActionsViewModel.ResultPath + "?" + query.TestBacktest(split, cell).ToQueryString();
        }

        return new ValidateResultViewModel(
            query.Symbol,
            strategy.Name,
            query.Timeframe.ToString(),
            OptimizeResultViewModel.FormatPeriod(settings.From, settings.To),
            metricLabel,
            $"{BacktestQuery.FormatNumber(query.TrainPercent)}% training",
            splitRows,
            grid,
            ResultHeaderViewModel.ExecutionRows(settings.ToBacktestRequest().Execution),
            permalink,
            pathBase + FormPath + "?" + query.ToQueryString(),
            pathBase + OptimizeResultViewModel.ResultPath + "?" + trainGrid.ToQueryString(),
            best,
            rows,
            reading,
            capitalNote,
            tested && test!.Trades.Count == 0,
            testUrl,
            heatmap,
            ValidateChartsViewModel.Create(heatmap, tested ? train : null, tested ? test : null));
    }

    /// <summary>
    /// The server sentence of the comparison: the optimised metric of each period, formatted exactly as in the table,
    /// side by side. No gap, ratio or percentage is computed (T3, "sans autre calcul"); an undefined value is "n/a".
    /// </summary>
    internal static string ReadingSentence(SweepMetric metric, PerformanceMetrics train, PerformanceMetrics test) =>
        $"{SweepMetrics.Label(metric)} with the best parameters: {SweepMetrics.Format(metric, SweepMetrics.Read(metric, train))} "
        + $"on the training period, {SweepMetrics.Format(metric, SweepMetrics.Read(metric, test))} on the test period.";

    private static string ParameterLabel(IStrategy strategy, string key) =>
        strategy.Parameters.FirstOrDefault(d => d.Key == key)?.Label ?? key;

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
}
