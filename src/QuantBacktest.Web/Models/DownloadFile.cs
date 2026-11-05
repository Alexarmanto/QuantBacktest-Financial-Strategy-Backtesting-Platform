using System.Globalization;
using System.Text;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// Content types and names of the downloads (docs/CONTRACT.md section 11, PART-4 section 7). The bytes themselves come
/// from IReportGenerator (Part 1): nothing is built here but the file name.
/// </summary>
internal static class DownloadFile
{
    public const string PdfContentType = "application/pdf";

    /// <summary>Exactly "text/csv", without a charset (decision D5): the CSV of section 11 is plain ASCII.</summary>
    public const string CsvContentType = "text/csv";

    public const string PdfExtension = ".pdf";
    public const string CsvExtension = ".csv";

    /// <summary>UTF-8 without a byte order mark (decision D5): the file starts with the header, never with EF BB BF.</summary>
    public static Encoding CsvEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string PdfFileName(BacktestResult result) => FileName(result, PdfExtension);

    public static string CsvFileName(BacktestResult result) => FileName(result, CsvExtension);

    /// <summary>
    /// QuantBacktest_&lt;SYMBOL&gt;_&lt;strategyId&gt;_&lt;yyyyMMdd&gt;, the date being the last bar of the period:
    /// BacktestResult.Bars holds the bars of the tested period only (docs/CONTRACT.md section 3). The same name with
    /// and without the Monte Carlo section: section 11 defines a single pattern.
    /// </summary>
    private static string FileName(BacktestResult result, string extension)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Bars.Count == 0)
        {
            // Decision D6: impossible by rule X1 (at least 2 bars); a result without bars is a bug, not a user error.
            throw new InvalidOperationException("The backtest result has no bar, so the download has no date (rule X1).");
        }

        var lastBar = result.Bars[^1].Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"QuantBacktest_{result.Request.Symbol}_{result.Request.StrategyId}_{lastBar}{extension}";
    }
}
