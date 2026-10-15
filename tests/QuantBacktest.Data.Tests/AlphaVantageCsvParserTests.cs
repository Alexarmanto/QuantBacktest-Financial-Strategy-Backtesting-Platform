using System.Globalization;
using Microsoft.Extensions.Logging;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public class AlphaVantageCsvParserTests
{
    private const string Raw = AlphaVantageCsvParser.RawHeader;
    private const string Adjusted = AlphaVantageCsvParser.AdjustedHeader;

    private readonly RecordingLoggerProvider _logs = new();

    [Fact]
    public void Parse_DescendingCsv_ReturnsBarsSortedAscending() // D5, P1-D3
    {
        var csv = $"{Raw}\n2026-01-07,12,13,11,12.5,300\n2026-01-06,11,12,10,11.5,200\n2026-01-05,10,11,9,10.5,100\n";

        var result = Parse(csv);

        result.Bars.Select(bar => bar.Date).Should().Equal(
            new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 7));
        result.Bars[0].Should().Be(new PriceBar(new DateOnly(2026, 1, 5), 10m, 11m, 9m, 10.5m, 100));
        result.IsAdjusted.Should().BeFalse();
    }

    [Fact]
    public void Parse_CrLfLineEndings_AreAccepted() // D5, P1-D3
    {
        var csv = $"{Raw}\r\n2026-01-06,11,12,10,11.5,200\r\n2026-01-05,10,11,9,10.5,100\r\n";

        var result = Parse(csv);

        result.Bars.Should().HaveCount(2);
        result.Bars[1].Volume.Should().Be(200);
    }

    [Fact]
    public void Parse_UnderFrenchCulture_ReadsTheDecimalPoint() // D5, P1-D3
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            var result = Parse($"{Raw}\n2026-01-05,123.45,124.5,122.25,123.75,1000\n");

            result.Bars[0].Open.Should().Be(123.45m);
            result.Bars[0].Low.Should().Be(122.25m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory] // P1-D3: an invalid line is skipped with a warning, the others are kept
    [InlineData("2026-01-06,11,12,10,11.5", "missing field")]
    [InlineData("2026-01-06,11,12,10,11.5,200,7", "extra field")]
    [InlineData("06/01/2026,11,12,10,11.5,200", "invalid date")]
    [InlineData("2026-01-06,abc,12,10,11.5,200", "invalid number")]
    [InlineData("2026-01-06,1,234.5,1,240.5,1,230.25,1,235,200", "thousands separators split the fields")]
    [InlineData("2026-01-06,0,12,10,11.5,200", "zero price")]
    [InlineData("2026-01-06,-11,12,10,11.5,200", "negative price")]
    [InlineData("2026-01-06,11,9,10,9.5,200", "high below low")]
    [InlineData("2026-01-06,11,12,10,12.5,200", "close above high")]
    [InlineData("2026-01-06,9.5,12,10,11,200", "open below low")]
    [InlineData("2026-01-06,11,12,10,11.5,-5", "negative volume")]
    [InlineData("2026-01-06,11,12,10,11.5,2.5", "fractional volume")]
    public void Parse_InvalidLine_IsSkippedWithAWarning(string invalidLine, string because)
    {
        var csv = $"{Raw}\n{invalidLine}\n2026-01-05,10,11,9,10.5,100\n";

        var result = Parse(csv);

        result.Bars.Should().ContainSingle(because).Which.Date.Should().Be(new DateOnly(2026, 1, 5));
        _logs.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("line 2"));
    }

    [Theory] // P1-D3: exact header, otherwise InvalidResponse
    [InlineData("")]
    [InlineData("date,open,high,low,close,volume\n2026-01-05,10,11,9,10.5,100")]
    [InlineData("timestamp,open,high,low,close\n2026-01-05,10,11,9,10.5")]
    [InlineData("<html>Service unavailable</html>")]
    public void Parse_InvalidHeader_ThrowsInvalidResponse(string csv)
    {
        var act = () => Parse(csv);

        act.Should().Throw<MarketDataException>().Which.Kind.Should().Be(MarketDataErrorKind.InvalidResponse);
    }

    [Theory] // P1-D3: no valid line, InvalidResponse
    [InlineData(Raw)]
    [InlineData(Raw + "\n")]
    [InlineData(Raw + "\n2026-01-05,0,0,0,0,0\n")]
    public void Parse_NoValidLine_ThrowsInvalidResponse(string csv)
    {
        var act = () => Parse(csv);

        act.Should().Throw<MarketDataException>().Which.Kind.Should().Be(MarketDataErrorKind.InvalidResponse);
    }

    [Fact]
    public void Parse_DuplicateDate_KeepsTheFirstLineAndWarns() // P1-D3: no duplicate in the output
    {
        var csv = $"{Raw}\n2026-01-05,10,11,9,10.5,100\n2026-01-05,20,21,19,20.5,999\n";

        var result = Parse(csv);

        result.Bars.Should().ContainSingle().Which.Open.Should().Be(10m);
        _logs.Entries.Should().ContainSingle(entry => entry.Message.Contains("duplicate"));
    }

    [Fact]
    public void Parse_AdjustedCsv_AppliesTheFactorToTheFourPricesAndKeepsTheRawVolume() // P1-D7, D6
    {
        // factor = adjusted close / close = 50 / 100 = 0.5
        var csv = $"{Adjusted}\n2026-01-09,98,104,96,100,50,123456,0.0000\n";

        var result = Parse(csv);

        result.IsAdjusted.Should().BeTrue();
        result.Bars.Should().ContainSingle().Which.Should().Be(
            new PriceBar(new DateOnly(2026, 1, 9), 49m, 52m, 48m, 50m, 123456));
    }

    [Fact]
    public void Parse_AdjustedCsv_RoundsToSixDecimalsWithoutBreakingTheInvariants() // P1-D7, CONTRACT section 3
    {
        // factor = 1 / 3: high = close, so both must stay equal after rounding.
        var csv = $"{Adjusted}\n2026-01-09,2,3,1,3,1,10,0\n";

        var bar = Parse(csv).Bars.Single();

        bar.Open.Should().Be(0.666667m);
        bar.High.Should().Be(1m);
        bar.Low.Should().Be(0.333333m);
        bar.Close.Should().Be(1m);
    }

    [Fact]
    public void Parse_RealisticSample_SatisfiesThePriceSeriesInvariants() // CONTRACT section 3, P1-D3
    {
        var csv = string.Join("\r\n",
            Adjusted,
            "2026-10-09,512.10,520.00,505.30,518.40,517.65,90123456,0.9100",
            "2026-10-02,500.00,515.25,498.10,512.00,511.26,88000000,0.0000",
            "2026-10-02,500.00,515.25,498.10,512.00,511.26,88000000,0.0000",
            "2026-09-25,505.00,503.00,495.00,499.00,498.28,70000000,0.0000",
            "2026-09-18,490.00,506.40,488.80,503.90,503.17,80000000,0.0000",
            string.Empty);

        var bars = Parse(csv).Bars;

        bars.Should().HaveCount(3);
        bars.Should().BeInAscendingOrder(bar => bar.Date);
        bars.Select(bar => bar.Date).Should().OnlyHaveUniqueItems();
        bars.Should().OnlyContain(bar => bar.Open > 0 && bar.High > 0 && bar.Low > 0 && bar.Close > 0);
        bars.Should().OnlyContain(bar => bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close));
    }

    private ParsedPrices Parse(string csv)
        => new AlphaVantageCsvParser(_logs.CreateLogger<AlphaVantageCsvParser>()).Parse(csv, "TEST Daily");
}
