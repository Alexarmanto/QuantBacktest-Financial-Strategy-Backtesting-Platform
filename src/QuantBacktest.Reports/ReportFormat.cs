using System.Globalization;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Reports;

/// <summary>
/// Display formats of the PDF (P1-R1, P1-R6), always in en-US whatever the culture of the machine.
/// Values are shown as they are; nothing is recomputed.
/// </summary>
internal static class ReportFormat
{
    public const string NotAvailable = "n/a";

    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A performance fraction as a signed percentage: 0.1234 gives "+12.34 %".</summary>
    public static string SignedPercent(double fraction)
        => (fraction * 100).ToString("+0.00;-0.00;0.00", Culture) + " %";

    /// <summary>A setting (fee, stop...) as a percentage without sign: 0.001 gives "0.10 %".</summary>
    public static string Percent(decimal fraction)
        => (fraction * 100m).ToString("0.00", Culture) + " %";

    public static string Percent(double fraction)
        => (fraction * 100).ToString("0.00", Culture) + " %";

    /// <summary>A drawdown (a non-negative fraction below the peak) shown as a loss: 0.2345 gives "-23.45 %".</summary>
    public static string Drawdown(double fraction) => SignedPercent(-fraction);

    /// <summary>An amount with thousands separators and 2 decimals: "10,000.00".</summary>
    public static string Money(decimal amount) => amount.ToString("N2", Culture);

    /// <summary>A ratio with 2 decimals, "n/a" when undefined.</summary>
    public static string Ratio(double? ratio) => ratio is { } value ? value.ToString("0.00", Culture) : NotAvailable;

    public static string Price(decimal price) => price.ToString("N2", Culture);

    public static string Quantity(decimal quantity) => quantity.ToString("#,0.####", Culture);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Integer(int value) => value.ToString("N0", Culture);

    public static string ParameterValue(double value) => value.ToString("0.####", Culture);

    public static string Timeframe(Timeframe timeframe) => timeframe switch
    {
        Core.Models.Timeframe.Daily => "Daily",
        Core.Models.Timeframe.Weekly => "Weekly",
        _ => timeframe.ToString(),
    };
}
