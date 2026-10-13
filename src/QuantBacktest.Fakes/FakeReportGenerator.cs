using System.Globalization;
using System.Text;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>Returns a tiny placeholder PDF (may not open in every viewer) and a CSV in the contract format.</summary>
public sealed class FakeReportGenerator : IReportGenerator
{
    private const string PlaceholderPdf =
        "%PDF-1.4\n" +
        "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
        "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
        "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 300 100]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj\n" +
        "4 0 obj<</Length 43>>stream\n" +
        "BT /F1 18 Tf 20 50 Td (FAKE REPORT) Tj ET\n" +
        "endstream endobj\n" +
        "5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n" +
        "trailer<</Root 1 0 R>>\n";

    public byte[] GeneratePdf(BacktestResult result, MonteCarloResult? monteCarlo = null) => Encoding.ASCII.GetBytes(PlaceholderPdf);

    public string GenerateTradesCsv(BacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new StringBuilder();
        builder.Append("entry_date,entry_price,exit_date,exit_price,quantity,fees,net_profit,return_pct,exit_reason,holding_bars\n");
        foreach (var trade in result.Trades)
        {
            builder.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"{trade.EntryDate:yyyy-MM-dd},{trade.EntryPrice},{trade.ExitDate:yyyy-MM-dd},{trade.ExitPrice},{trade.Quantity},{trade.Fees},{trade.NetProfit},{trade.ReturnFraction * 100.0},{trade.ExitReason},{trade.HoldingBars}\n"));
        }

        return builder.ToString();
    }
}
