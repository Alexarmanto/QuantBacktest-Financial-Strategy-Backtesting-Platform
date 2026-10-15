using QuantBacktest.Core.Models;
using ScottPlot;
using ScottPlot.TickGenerators;

namespace QuantBacktest.Reports.Charts;

/// <summary>Draws the report charts as PNG images with ScottPlot (P1-R1). Values are plotted as they are.</summary>
internal static class ChartRenderer
{
    public const int Width = 1000;
    public const int Height = 420;

    private static readonly Color StrategyColor = Color.FromHex("#1f6feb");
    private static readonly Color BenchmarkColor = Color.FromHex("#8b949e");

    /// <summary>P1-R2: strategy equity against the buy &amp; hold benchmark.</summary>
    public static byte[] Equity(BacktestResult result)
    {
        var plot = NewPlot("Equity: strategy vs buy & hold");
        var dates = result.Equity.Select(point => ToDateTime(point.Date)).ToArray();
        AddLine(plot, dates, result.Equity.Select(point => (double)point.StrategyEquity).ToArray(), "Strategy", StrategyColor);
        AddLine(plot, dates, result.Equity.Select(point => (double)point.BenchmarkEquity).ToArray(), "Buy & hold", BenchmarkColor);
        plot.Axes.Left.TickGenerator = new NumericAutomatic
        {
            LabelFormatter = value => value.ToString("N0", ReportFormat.Culture),
        };
        return Render(plot);
    }

    internal static Plot NewPlot(string title)
    {
        var plot = new Plot();
        plot.Title(title);
        plot.Axes.DateTimeTicksBottom();
        plot.Legend.Alignment = Alignment.UpperLeft;
        return plot;
    }

    internal static void AddLine(Plot plot, DateTime[] dates, double[] values, string label, Color color)
    {
        if (dates.Length == 0)
        {
            return;
        }

        var line = plot.Add.Scatter(dates, values);
        line.LegendText = label;
        line.Color = color;
        line.MarkerSize = 0;
        line.LineWidth = 2;
    }

    internal static byte[] Render(Plot plot)
    {
        plot.ShowLegend();
        return plot.GetImageBytes(Width, Height, ImageFormat.Png);
    }

    internal static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
}
