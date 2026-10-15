using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Reports.Pdf;
using QuestPDF.Fluent;

namespace QuantBacktest.Reports;

/// <summary>
/// Part 1 implementation of <see cref="IReportGenerator"/>. Prints the result as it is: no metric is recomputed;
/// a wrong figure is a bug of the part that computed it.
/// </summary>
internal sealed class PdfReportGenerator(TimeProvider time) : IReportGenerator
{
    public byte[] GeneratePdf(BacktestResult result, MonteCarloResult? monteCarlo = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new BacktestReportDocument(result, time.GetUtcNow()).GeneratePdf();
    }

    public string GenerateTradesCsv(BacktestResult result)
        => throw new NotSupportedException("The trades CSV comes with PR 7 of docs/parts/PART-1.md.");
}
