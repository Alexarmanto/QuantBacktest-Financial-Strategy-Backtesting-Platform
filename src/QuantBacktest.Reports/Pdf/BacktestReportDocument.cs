using QuantBacktest.Core.Models;
using QuantBacktest.Reports.Charts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QuantBacktest.Reports.Pdf;

/// <summary>The QuestPDF layout of the report: A4 portrait, English (P1-R1, P1-R2, P1-R5).</summary>
internal sealed class BacktestReportDocument(BacktestResult result, DateTimeOffset generatedAt) : IDocument
{
    private const string AccentColor = "#1f6feb";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{ReportContent.Title} - {result.Request.Symbol} - {result.Request.StrategyId}",
        Author = "QuantBacktest",
        Creator = "QuantBacktest",
        CreationDate = generatedAt,
        ModifiedDate = generatedAt,
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontSize(9));
            page.Content().Column(column =>
            {
                column.Spacing(10);
                ComposeSummaryPage(column);
            });
            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(ReportContent.Footer(generatedAt)).FontSize(7).FontColor(Colors.Grey.Darken1);
                row.AutoItem().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });
    }

    /// <summary>P1-R2: title, settings, unadjusted warning, metrics table and equity curve.</summary>
    private void ComposeSummaryPage(ColumnDescriptor column)
    {
        column.Item().Text(ReportContent.Title).FontSize(18).Bold().FontColor(AccentColor);

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(90);
                columns.RelativeColumn();
            });
            foreach (var (label, value) in ReportContent.Summary(result))
            {
                table.Cell().PaddingVertical(1).Text(label).SemiBold();
                table.Cell().PaddingVertical(1).Text(value);
            }
        });

        if (!result.PricesAdjusted)
        {
            column.Item().Background(Colors.Amber.Lighten4).Padding(5).Text(ReportContent.UnadjustedWarning).Bold();
        }

        column.Item().Text("Strategy vs Buy & hold").FontSize(12).Bold();
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            table.Header(header =>
            {
                HeaderCell(header.Cell(), "Metric");
                HeaderCell(header.Cell().AlignRight(), "Strategy");
                HeaderCell(header.Cell().AlignRight(), "Buy & hold");
            });

            var index = 0;
            foreach (var row in ReportContent.Metrics(result))
            {
                var background = index++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                table.Cell().Background(background).Padding(2).Text(row.Label);
                table.Cell().Background(background).Padding(2).AlignRight().Text(row.Strategy);
                table.Cell().Background(background).Padding(2).AlignRight().Text(row.Benchmark);
            }
        });

        column.Item().Image(ChartRenderer.Equity(result)).FitWidth();
    }

    private static void HeaderCell(IContainer cell, string text)
        => cell.BorderBottom(1).BorderColor(Colors.Grey.Medium).Padding(2).Text(text).Bold();
}
