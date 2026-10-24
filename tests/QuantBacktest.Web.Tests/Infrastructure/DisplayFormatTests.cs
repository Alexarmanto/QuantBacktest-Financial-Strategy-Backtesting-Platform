using System.Globalization;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Tests.Infrastructure;

public class DisplayFormatTests
{
    [Theory] // P4-U14
    [InlineData("10371.39", "$10,371.39")]
    [InlineData("0", "$0.00")]
    [InlineData("1234567.891", "$1,234,567.89")]
    [InlineData("0.005", "$0.01")]
    [InlineData("-10.5", "-$10.50")]
    [InlineData("-10371.39", "-$10,371.39")]
    [InlineData("-0.004", "$0.00")]
    public void Money_UsesUsDollarFormat_WithExplicitNegativeSign(string amount, string expected)
    {
        DisplayFormat.Money(decimal.Parse(amount, CultureInfo.InvariantCulture)).Should().Be(expected);
    }

    [Theory] // P4-U14: fractions in the code, percentages only on display (CONTRACT section 3)
    [InlineData(0.1234, "12.34%")]
    [InlineData(-0.0567, "-5.67%")]
    [InlineData(0, "0.00%")]
    [InlineData(12.3456, "1,234.56%")]
    [InlineData(-0.00001, "0.00%")]
    public void Percent_ShowsTwoDecimals(double fraction, string expected)
    {
        DisplayFormat.Percent(fraction).Should().Be(expected);
    }

    [Fact]
    public void Ratio_Null_IsNotAvailable() // P4-U4, CONTRACT section 3 (undefined ratio)
    {
        DisplayFormat.Ratio(null).Should().Be("n/a");
    }

    [Theory] // P4-U14
    [InlineData(1.23456, "1.23")]
    [InlineData(-0.5, "-0.50")]
    public void Ratio_ShowsTwoDecimals(double value, string expected)
    {
        DisplayFormat.Ratio(value).Should().Be(expected);
    }

    [Fact]
    public void Formats_DoNotDependOnTheCurrentCulture() // P4-U14
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            DisplayFormat.Money(-10371.39m).Should().Be("-$10,371.39");
            DisplayFormat.Percent(0.1234).Should().Be("12.34%");
            DisplayFormat.Ratio(1.5).Should().Be("1.50");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
