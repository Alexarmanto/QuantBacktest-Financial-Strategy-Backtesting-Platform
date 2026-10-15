using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Reports.Tests;

public class TradesCsvTests
{
    private static readonly Trade[] HandMadeTrades =
    [
        new(new DateOnly(2026, 1, 5), 100.5m, new DateOnly(2026, 1, 9), 110.25m, 99.004975m, 20.86m, 944.57m, 0.0944575, ExitReason.Signal, 4),
        new(new DateOnly(2026, 2, 2), 1234.5m, new DateOnly(2026, 2, 3), 1172.775m, 8.1m, 19.5m, -519.47m, -0.0519, ExitReason.StopLoss, 0),
        new(new DateOnly(2026, 3, 2), 50m, new DateOnly(2026, 3, 31), 55m, 10m, 1.05m, 48.95m, 0.0979, ExitReason.EndOfData, 22),
    ];

    [Fact]
    public void GenerateTradesCsv_MatchesTheContractFormatCharacterForCharacter() // P1-R7, CONTRACT section 11
    {
        var result = TestResults.Create() with { Trades = HandMadeTrades };

        var csv = CreateGenerator().GenerateTradesCsv(result);

        csv.Should().Be(
            "entry_date,entry_price,exit_date,exit_price,quantity,fees,net_profit,return_pct,exit_reason,holding_bars\n"
            + "2026-01-05,100.5,2026-01-09,110.25,99.004975,20.86,944.57,9.44575,Signal,4\n"
            + "2026-02-02,1234.5,2026-02-03,1172.775,8.1,19.5,-519.47,-5.19,StopLoss,0\n"
            + "2026-03-02,50,2026-03-31,55,10,1.05,48.95,9.79,EndOfData,22\n");
    }

    [Fact]
    public void GenerateTradesCsv_WithoutTrades_KeepsTheHeader() // P1-R7: header always present
        => CreateGenerator().GenerateTradesCsv(TestResults.Create(tradeCount: 0))
            .Should().Be("entry_date,entry_price,exit_date,exit_price,quantity,fees,net_profit,return_pct,exit_reason,holding_bars\n");

    [Fact]
    public void GenerateTradesCsv_UnderFrenchCulture_UsesTheDecimalPoint() // P1-R7, CONTRACT section 11
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            var csv = CreateGenerator().GenerateTradesCsv(TestResults.Create() with { Trades = HandMadeTrades });

            csv.Split('\n')[2].Should().Be("2026-02-02,1234.5,2026-02-03,1172.775,8.1,19.5,-519.47,-5.19,StopLoss,0");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GenerateTradesCsv_HasOneLinePerTradeAndOnlyLineFeeds() // P1-R7
    {
        var csv = CreateGenerator().GenerateTradesCsv(TestResults.Create(barCount: 500, tradeCount: 1000));

        csv.Should().NotContain("\r");
        csv.Split('\n').Should().HaveCount(1 + 1000 + 1, "header, 1000 trades and the empty string after the last line feed");
        csv.Split('\n').Skip(1).SkipLast(1).Should().OnlyContain(line => line.Split(',').Length == 10);
    }

    [Theory] // P1-R7: RFC 4180 escaping
    [InlineData("Signal", "Signal")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    public void Escape_QuotesOnlyWhenNeeded(string field, string expected)
        => TradesCsv.Escape(field).Should().Be(expected);

    private static IReportGenerator CreateGenerator()
        => new ServiceCollection().AddQuantBacktestReports().BuildServiceProvider().GetRequiredService<IReportGenerator>();
}
