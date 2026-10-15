using System.Globalization;
using System.Text;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Reports;

/// <summary>The trade log as CSV, exactly as docs/CONTRACT.md section 11 defines it (P1-R7).</summary>
internal static class TradesCsv
{
    public const string Header = "entry_date,entry_price,exit_date,exit_price,quantity,fees,net_profit,return_pct,exit_reason,holding_bars";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Write(BacktestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var csv = new StringBuilder();
        csv.Append(Header).Append('\n');
        foreach (var trade in result.Trades)
        {
            string[] fields =
            [
                trade.EntryDate.ToString("yyyy-MM-dd", Invariant),
                trade.EntryPrice.ToString(Invariant),
                trade.ExitDate.ToString("yyyy-MM-dd", Invariant),
                trade.ExitPrice.ToString(Invariant),
                trade.Quantity.ToString(Invariant),
                trade.Fees.ToString(Invariant),
                trade.NetProfit.ToString(Invariant),
                // A fraction in the model, a percentage in the file; 12 decimals hide the binary noise of the product.
                (trade.ReturnFraction * 100).ToString("0.############", Invariant),
                trade.ExitReason.ToString(),
                trade.HoldingBars.ToString(Invariant),
            ];
            csv.AppendJoin(',', fields.Select(Escape)).Append('\n');
        }

        return csv.ToString();
    }

    /// <summary>RFC 4180: a field holding a comma, a quote or a line break is quoted, inner quotes doubled.</summary>
    internal static string Escape(string field)
        => field.AsSpan().IndexOfAny(",\"\r\n") < 0 ? field : $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
