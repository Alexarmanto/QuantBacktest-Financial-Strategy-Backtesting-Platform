using System.Globalization;

namespace QuantBacktest.Web.Infrastructure;

/// <summary>
/// Presentation formats of P4-U14. The patterns are spelled out with the invariant culture
/// ("," groups, "." decimals) so the output never depends on the ICU data of the machine.
/// </summary>
internal static class DisplayFormat
{
    public const string NotAvailable = "n/a";

    private const string TwoDecimals = "#,##0.00";

    /// <summary>US dollar amount: $10,371.39 and -$10.50.</summary>
    public static string Money(decimal amount)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var magnitude = Math.Abs(rounded).ToString(TwoDecimals, CultureInfo.InvariantCulture);
        return rounded < 0 ? "-$" + magnitude : "$" + magnitude;
    }

    /// <summary>Fraction shown as a percentage with 2 decimals: 0.1234 → 12.34%.</summary>
    public static string Percent(double fraction) => Number(fraction * 100) + "%";

    /// <summary>Ratio with 2 decimals; an undefined ratio (null, docs/CONTRACT.md section 3) is "n/a".</summary>
    public static string Ratio(double? value) => value is { } defined ? Number(defined) : NotAvailable;

    private static string Number(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);

        // -0.001 rounds to -0.0, which would print as "-0.00".
        return (rounded == 0 ? 0 : rounded).ToString(TwoDecimals, CultureInfo.InvariantCulture);
    }
}
