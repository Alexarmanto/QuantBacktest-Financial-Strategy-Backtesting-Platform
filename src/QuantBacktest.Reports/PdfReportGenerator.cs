using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Reports.Pdf;
using QuestPDF.Fluent;

namespace QuantBacktest.Reports;

/// <summary>
/// Part 1 implementation of <see cref="IReportGenerator"/>. Prints the result as it is: no metric is recomputed;
/// a wrong figure is a bug of the part that computed it.
/// </summary>
/// <param name="catalog">
/// Part 2's catalog, read only to print the rules of a no-code strategy (P1-X1); BacktestResult does not carry them.
/// Optional: without it, the rules are left out.
/// </param>
internal sealed class PdfReportGenerator(TimeProvider time, IStrategyCatalog? catalog = null) : IReportGenerator
{
    public byte[] GeneratePdf(BacktestResult result, MonteCarloResult? monteCarlo = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new BacktestReportDocument(result, monteCarlo, FindCustomRules(result.Request.StrategyId), time.GetUtcNow()).GeneratePdf();
    }

    /// <summary>P1-R7: format of docs/CONTRACT.md section 11.</summary>
    public string GenerateTradesCsv(BacktestResult result) => TradesCsv.Write(result);

    internal StrategyDocumentation? FindCustomRules(string strategyId)
    {
        if (catalog is null || !strategyId.StartsWith(Conventions.CustomStrategyIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // IReportGenerator is synchronous (contract); the catalog lookup is a single local read.
        var strategy = catalog.FindAsync(strategyId).GetAwaiter().GetResult();
        return strategy?.Category == StrategyCategory.Custom ? strategy.Documentation : null;
    }
}
