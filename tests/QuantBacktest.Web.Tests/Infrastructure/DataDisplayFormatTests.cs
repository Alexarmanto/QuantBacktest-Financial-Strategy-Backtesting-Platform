using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Tests.Infrastructure;

/// <summary>The formats added for the data pages (P4-U10, P4-U14). DisplayFormatTests is left as it is.</summary>
public class DataDisplayFormatTests
{
    [Fact] // P4-U14: market dates yyyy-MM-dd, invariant
    public void Date_IsIso()
    {
        DisplayFormat.Date(new DateOnly(2026, 5, 2)).Should().Be("2026-05-02");
    }

    [Theory] // P4-U14: counts with grouped thousands, invariant
    [InlineData(0, "0")]
    [InlineData(100, "100")]
    [InlineData(1406, "1,406")]
    public void Integer_IsGrouped(int value, string expected)
    {
        DisplayFormat.Integer(value).Should().Be(expected);
    }

    [Theory] // P4-U10: LastFetchedUtc and ResetsAtUtc shown in UTC, whatever the offset
    [InlineData(0, "2026-11-06 23:03 UTC")]
    [InlineData(2, "2026-11-06 21:03 UTC")]
    public void UtcTimestamp_ConvertsTheOffsetToUtc(int offsetHours, string expected)
    {
        var instant = new DateTimeOffset(2026, 11, 6, 23, 3, 53, TimeSpan.FromHours(offsetHours));

        DisplayFormat.UtcTimestamp(instant).Should().Be(expected);
    }
}
