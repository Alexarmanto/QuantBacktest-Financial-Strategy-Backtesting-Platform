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
    private static readonly Color DrawdownColor = Color.FromHex("#cf222e");
    private static readonly Color CloseColor = Color.FromHex("#24292f");
    private static readonly Color BuyColor = Color.FromHex("#1a7f37");
    private static readonly Color SellColor = Color.FromHex("#cf222e");
    private static readonly Color[] IndicatorColors =
        [Color.FromHex("#1f6feb"), Color.FromHex("#bf8700"), Color.FromHex("#8250df"), Color.FromHex("#0a7ea4")];

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

    /// <summary>P1-R3: drawdown of the strategy, shown below zero as a loss.</summary>
    public static byte[] Drawdown(BacktestResult result)
    {
        var plot = NewPlot("Strategy drawdown");
        var dates = result.Equity.Select(point => ToDateTime(point.Date)).ToArray();
        if (dates.Length > 0)
        {
            var area = plot.Add.Scatter(dates, result.Equity.Select(point => -point.StrategyDrawdown * 100).ToArray());
            area.LegendText = "Drawdown";
            area.Color = DrawdownColor;
            area.MarkerSize = 0;
            area.FillY = true;
            area.FillYColor = DrawdownColor.WithAlpha(0.3);
        }

        plot.Axes.Left.TickGenerator = new NumericAutomatic
        {
            LabelFormatter = value => value.ToString("0.#", ReportFormat.Culture) + " %",
        };
        return Render(plot);
    }

    /// <summary>
    /// P1-R3: closes with the Price-pane indicators, buy triangles (up) and sell triangles (down)
    /// at the dates and fill prices of the trades.
    /// </summary>
    public static byte[] Price(BacktestResult result)
    {
        var plot = NewPlot($"{result.Request.Symbol} close, indicators and trades");
        var dates = result.Bars.Select(bar => ToDateTime(bar.Date)).ToArray();
        AddLine(plot, dates, result.Bars.Select(bar => (double)bar.Close).ToArray(), "Close", CloseColor);

        var index = 0;
        foreach (var indicator in result.Indicators.Where(indicator => indicator.Pane == IndicatorPane.Price))
        {
            AddIndicator(plot, dates, indicator, IndicatorColors[index++ % IndicatorColors.Length]);
        }

        AddTradeMarkers(plot, result.Trades.Select(trade => (trade.EntryDate, trade.EntryPrice)), MarkerShape.FilledTriangleUp, BuyColor, "Buy");
        AddTradeMarkers(plot, result.Trades.Select(trade => (trade.ExitDate, trade.ExitPrice)), MarkerShape.FilledTriangleDown, SellColor, "Sell");
        plot.Axes.Left.TickGenerator = new NumericAutomatic
        {
            LabelFormatter = value => value.ToString("N2", ReportFormat.Culture),
        };
        return Render(plot);
    }

    /// <summary>P1-X1: the Oscillator-pane indicators with their reference lines (RSI 30 and 70...); null when there is none.</summary>
    public static byte[]? Oscillators(BacktestResult result)
    {
        var oscillators = result.Indicators.Where(indicator => indicator.Pane == IndicatorPane.Oscillator).ToList();
        if (oscillators.Count == 0)
        {
            return null;
        }

        var plot = NewPlot("Oscillators");
        var dates = result.Bars.Select(bar => ToDateTime(bar.Date)).ToArray();
        var index = 0;
        foreach (var indicator in oscillators)
        {
            AddIndicator(plot, dates, indicator, IndicatorColors[index++ % IndicatorColors.Length]);
        }

        foreach (var level in oscillators.SelectMany(indicator => indicator.ReferenceLines).Where(double.IsFinite).Distinct())
        {
            var line = plot.Add.HorizontalLine(level);
            line.Color = BenchmarkColor;
            line.LinePattern = LinePattern.Dashed;
            line.LineWidth = 1;
        }

        return Render(plot);
    }

    /// <summary>
    /// P1-X1: a Monte Carlo histogram, one bar per bin as given. Bins are fractions; drawdowns are shown as losses.
    /// </summary>
    public static byte[] Histogram(IReadOnlyList<HistogramBin> bins, string title, bool asLoss)
    {
        var plot = new Plot();
        plot.Title(title);
        var sign = asLoss ? -1 : 1;
        var bars = bins.Select(bin => new Bar
        {
            Position = sign * (bin.Lower + bin.Upper) / 2 * 100,
            Value = bin.Count,
            Size = Math.Max((bin.Upper - bin.Lower) * 100, 1e-6) * 0.95,
            FillColor = asLoss ? DrawdownColor : StrategyColor,
            LineWidth = 0,
        }).ToList();
        if (bars.Count > 0)
        {
            plot.Add.Bars(bars);
        }

        plot.Axes.Margins(bottom: 0);
        plot.Axes.Bottom.Label.Text = asLoss ? "Max drawdown (%)" : "Final return (%)";
        plot.Axes.Left.Label.Text = "Simulations";
        plot.Axes.Bottom.TickGenerator = new NumericAutomatic
        {
            LabelFormatter = value => value.ToString("0.#", ReportFormat.Culture),
        };
        return plot.GetImageBytes(Width / 2, Height, ImageFormat.Png);
    }

    /// <summary>Warm-up values are null and are not drawn; values follow the bars one to one.</summary>
    internal static void AddIndicator(Plot plot, DateTime[] dates, IndicatorSeries indicator, Color color)
    {
        var defined = Enumerable.Range(0, Math.Min(dates.Length, indicator.Values.Count))
            .Where(i => indicator.Values[i] is { } value && double.IsFinite(value))
            .ToList();
        AddLine(plot, [.. defined.Select(i => dates[i])], [.. defined.Select(i => indicator.Values[i]!.Value)], indicator.Label, color);
    }

    private static void AddTradeMarkers(Plot plot, IEnumerable<(DateOnly Date, decimal Price)> points, MarkerShape shape, Color color, string label)
    {
        var list = points.ToList();
        if (list.Count == 0)
        {
            return;
        }

        var markers = plot.Add.Markers(
            list.Select(point => ToDateTime(point.Date).ToOADate()).ToArray(),
            list.Select(point => (double)point.Price).ToArray(),
            shape,
            9,
            color);
        markers.LegendText = label;
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
