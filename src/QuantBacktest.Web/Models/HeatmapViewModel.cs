using System.Globalization;
using System.Text.Json;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One cell of the heatmap as drawn by heatmap.js and listed in the table. Every text is formatted here (P4-U14);
/// Value is SweepCell.Value as returned by the contract (null when undefined, invalid, or not finite), never recomputed.
/// Url is the canonical /Backtest/Result of the cell; null for an invalid combination (the backtest would be refused).
/// Side and T place the colour (HeatmapViewModel.Ranks): Side -1 below the centre, +1 above, 0 equal to it; T in [0, 1]
/// is the rank of the distance to the centre on that side. Both null without a value.
/// </summary>
internal sealed record HeatmapCell(
    int Xi,
    int Yi,
    double? Value,
    string XText,
    string YText,
    string ValueText,
    string TradesText,
    string? Note,
    bool Invalid,
    bool Dim,
    bool Best,
    string? Url,
    int? Side = null,
    double? T = null);

/// <summary>One line of the text alternative (table in a details element), sortable by table-sort.js.</summary>
internal sealed record HeatmapRow(MetricCell X, MetricCell Y, MetricCell Value, MetricCell Trades, string? Note, string? Url, bool Best);

/// <summary>
/// The optimisation heatmap (P4-X2, O1 to O5): the JSON read by heatmap.js, the legend, the reading sentence, the
/// overfitting note and banner, and the table of every cell. Everything comes from SweepResult: the best cell is
/// SweepResult.Best (O3), the warning is SweepResult.IsIsolatedPeak (O4); this model formats and builds URLs only.
/// Reused by the train/test page (P4-X3) with <c>linkCells: false</c>.
/// </summary>
internal sealed record HeatmapViewModel(
    string Json,
    string MetricLabel,
    string XLabel,
    string YLabel,
    int Columns,
    int Rows,
    string Summary,
    string Reading,
    bool HasBest,
    bool IsIsolatedPeak,
    string? ScaleLow,
    string ScaleCentre,
    string? ScaleHigh,
    IReadOnlyList<HeatmapRow> TableRows)
{
    public const string DataId = "optimize-heatmap-data";
    public const string CanvasId = "chart-heatmap";

    /// <summary>P4-X2: "cellule avec TradeCount &lt; 5 atténuée (infobulle « fewer than 5 trades »)".</summary>
    public const int FewTradesThreshold = 5;

    public const string FewTradesNote = "Fewer than 5 trades.";
    public const string UndefinedNote = "Metric undefined (n/a).";
    public const string BestNote = "Best cell.";

    // P4-X2, word for word.
    public const string OptimisticNote =
        "The best cell is optimistic by construction: validate it on unseen data (Validate page).";

    public const string IsolatedPeakBanner =
        "Isolated peak: the best result is much better than its neighbours, a classic sign of overfitting.";

    public const string NoBestMessage = "No combination produced a value, so there is no best cell.";

    public const string NoValueLegend = "No value";
    public const string FewTradesLegend = "Fewer than 5 trades";
    public const string BestLegend = "Best cell";
    public const string TradesLabel = "Trades";

    public const string RankNote = "Colour shows rank on each side of the centre; hover or open the table for exact values.";
    public const string NoteLabel = "Note";

    /// <param name="result">The answer of IParameterSweepService for <paramref name="query"/>.</param>
    /// <param name="query">The resolved (canonical) query: the source of the cell URLs.</param>
    /// <param name="linkCells">False for a heatmap whose cells must not open a backtest (train/test).</param>
    public static HeatmapViewModel Create(
        SweepResult result,
        IStrategy strategy,
        OptimizationQuery query,
        string pathBase,
        bool linkCells = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(pathBase);

        var metric = query.Metric;
        var metricLabel = SweepMetrics.Label(metric);
        var xLabel = ParameterLabel(strategy, query.X.Key!);
        var yLabel = ParameterLabel(strategy, query.Y.Key!);
        var columns = result.XValues.Count;
        var rows = result.YValues.Count;
        var bestIndex = result.Best is null ? -1 : IndexOf(result.Cells, result.Best);

        var cells = new List<HeatmapCell>(result.Cells.Count);
        for (var i = 0; i < result.Cells.Count; i++)
        {
            var cell = result.Cells[i];

            // O5: row-major order, for each Y every X.
            var value = cell.Value is { } v && double.IsFinite(v) ? v : (double?)null;
            var invalid = cell.Error is not null;
            var fewTrades = !invalid && cell.TradeCount < FewTradesThreshold;
            var best = i == bestIndex;
            var notes = new List<string>();
            if (best)
            {
                notes.Add(BestNote);
            }

            if (invalid)
            {
                notes.Add(cell.Error!);
            }
            else if (value is null)
            {
                notes.Add(UndefinedNote);
            }

            if (fewTrades)
            {
                notes.Add(FewTradesNote);
            }

            var url = linkCells && !invalid
                ? pathBase + ResultActionsViewModel.ResultPath + "?" + query.ForCell(cell.X, cell.Y).ToQueryString()
                : null;
            cells.Add(new HeatmapCell(
                columns == 0 ? 0 : i % columns,
                columns == 0 ? 0 : i / columns,
                value,
                BacktestQuery.FormatNumber(cell.X),
                BacktestQuery.FormatNumber(cell.Y),
                SweepMetrics.Format(metric, value),
                cell.TradeCount.ToString(CultureInfo.InvariantCulture),
                notes.Count == 0 ? null : string.Join(" ", notes),
                invalid,
                fewTrades && value is not null,
                best,
                url));
        }

        var centre = SweepMetrics.Centre(metric);
        var ranks = Ranks(centre, cells.Select(c => c.Value).ToList());
        for (var i = 0; i < cells.Count; i++)
        {
            if (ranks[i] is { } rank)
            {
                cells[i] = cells[i] with { Side = rank.Side, T = rank.T };
            }
        }

        var payload = new
        {
            metricLabel,
            xLabel,
            yLabel,
            tradesLabel = TradesLabel,
            xLabels = result.XValues.Select(BacktestQuery.FormatNumber).ToList(),
            yLabels = result.YValues.Select(BacktestQuery.FormatNumber).ToList(),
            cells,
        };

        var tableRows = cells.Select((c, i) => new HeatmapRow(
                new MetricCell(c.XText, ResultTradesViewModel.SortNumber(result.Cells[i].X)),
                new MetricCell(c.YText, ResultTradesViewModel.SortNumber(result.Cells[i].Y)),
                new MetricCell(c.ValueText, c.Value is { } v ? ResultTradesViewModel.SortNumber(v) : null,
                    SweepMetrics.IsReturn(metric) && c.Value is { } r ? ValueTone.Of(r) : ValueTone.Neutral),
                new MetricCell(c.TradesText, c.TradesText),
                c.Note,
                c.Url,
                c.Best))
            .ToList();

        return new HeatmapViewModel(
            JsonSerializer.Serialize(payload, ResultChartsViewModel.JsonOptions),
            metricLabel,
            xLabel,
            yLabel,
            columns,
            rows,
            $"Heatmap of the {metricLabel} for {Count(cells.Count)} combinations of {xLabel} (X axis) and {yLabel} (Y axis).",
            ReadingSentence(result, metric, metricLabel, xLabel, yLabel, cells),
            result.Best is not null,
            result.IsIsolatedPeak,
            Farthest(metric, centre, cells, -1),
            SweepMetrics.CentreLabel(metric),
            Farthest(metric, centre, cells, 1),
            tableRows);
    }

    /// <summary>Label of a parameter in the catalog (the key itself if the catalog does not know it).</summary>
    private static string ParameterLabel(IStrategy strategy, string key) =>
        strategy.Parameters.FirstOrDefault(d => d.Key == key)?.Label ?? key;

    /// <summary>The position of the contract's best cell; its value is never compared with the others here (O3).</summary>
    private static int IndexOf(IReadOnlyList<SweepCell> cells, SweepCell best)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (ReferenceEquals(cells[i], best))
            {
                return i;
            }
        }

        for (var i = 0; i < cells.Count; i++)
        {
            if (cells[i] == best)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Colour by rank on each side of the centre (decision of the review: a linear scale is unreadable on stretched
    /// distributions). Presentation only: no metric is changed, the exact values stay in the tooltip and the table.
    /// For each side (values below the centre, values above it; a value equal to the centre is side 0, t 0), the N
    /// distances to the centre are ranked from 1 (closest) to N (farthest) and t = (r − 1) / (N − 1); N = 1 gives t = 1.
    /// Ties (equal distances, exact double equality) share the AVERAGE of their ranks, so equal values get the same t
    /// and the colours stay spread evenly.
    /// </summary>
    internal static (int Side, double T)?[] Ranks(double centre, IReadOnlyList<double?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new (int Side, double T)?[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is { } v && v == centre)
            {
                result[i] = (0, 0);
            }
        }

        foreach (var side in new[] { -1, 1 })
        {
            var members = Enumerable.Range(0, values.Count)
                .Where(i => values[i] is { } v && Math.Sign(v - centre) == side)
                .OrderBy(i => Math.Abs(values[i]!.Value - centre))
                .ToList();
            var count = members.Count;
            var position = 0;
            while (position < count)
            {
                // members[position..end) share the same distance: ranks position + 1 to end, average (position + 1 + end) / 2.
                var distance = Math.Abs(values[members[position]]!.Value - centre);
                var end = position;
                while (end < count && Math.Abs(values[members[end]]!.Value - centre) == distance)
                {
                    end++;
                }

                var averageRank = (position + 1 + end) / 2.0;
                var t = count == 1 ? 1 : (averageRank - 1) / (count - 1);
                for (var k = position; k < end; k++)
                {
                    result[members[k]] = (side, t);
                }

                position = end;
            }
        }

        return result;
    }

    /// <summary>The legend text of one side: its value farthest from the centre, exactly as in the grid; null when the side is empty.</summary>
    private static string? Farthest(SweepMetric metric, double centre, IReadOnlyList<HeatmapCell> cells, int side)
    {
        var farthest = cells
            .Where(c => c.Side == side)
            .OrderByDescending(c => Math.Abs(c.Value!.Value - centre))
            .FirstOrDefault();
        return farthest is null ? null : SweepMetrics.Format(metric, farthest.Value);
    }

    private static string ReadingSentence(
        SweepResult result,
        SweepMetric metric,
        string metricLabel,
        string xLabel,
        string yLabel,
        IReadOnlyList<HeatmapCell> cells)
    {
        var withoutValue = cells.Count(c => c.Value is null);
        var counts = $"{Count(cells.Count)} cells, {Count(withoutValue)} without a value.";
        if (result.Best is not { } best)
        {
            return $"{NoBestMessage} {counts}";
        }

        var text = $"Best {metricLabel}: {SweepMetrics.Format(metric, best.Value)} at {xLabel} = "
            + $"{BacktestQuery.FormatNumber(best.X)} and {yLabel} = {BacktestQuery.FormatNumber(best.Y)}";
        if (result.BestNeighborhoodAverage is { } average)
        {
            text += $"; average of its neighbours: {SweepMetrics.Format(metric, average)}";
        }

        return $"{text}. {counts}";
    }

    private static string Count(int count) => count.ToString("#,##0", CultureInfo.InvariantCulture);
}
