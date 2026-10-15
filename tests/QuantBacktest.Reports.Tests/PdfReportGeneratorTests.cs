using System.Text;
using System.Text.RegularExpressions;
using QuantBacktest.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Reports.Charts;

namespace QuantBacktest.Reports.Tests;

public class PdfReportGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 10, 15, 14, 5, 0, TimeSpan.Zero);

    [Fact]
    public void GeneratePdf_ProducesAPdfDocument() // P1-R1
    {
        var pdf = CreateGenerator().GeneratePdf(TestResults.Create());

        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void GeneratePdf_IsA4Portrait() // P1-R1: 595 x 842 points
    {
        var pdf = Encoding.Latin1.GetString(CreateGenerator().GeneratePdf(TestResults.Create()));

        pdf.Should().Contain("/MediaBox [0 0 595 842]");
    }

    [Fact]
    public void GeneratePdf_TakesItsDateFromTheInjectedClock() // P1-R5
    {
        var pdf = Encoding.Latin1.GetString(CreateGenerator().GeneratePdf(TestResults.Create()));

        pdf.Should().Contain("D:20261015140500");
    }

    [Theory] // P1-R8: never an exception
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(60, 0)]
    [InlineData(60, 1)]
    [InlineData(500, 1000)]
    public void GeneratePdf_SmallAndLargeResults_DoNotThrow(int barCount, int tradeCount)
    {
        var pdf = CreateGenerator().GeneratePdf(TestResults.Create(barCount, tradeCount));

        pdf.Should().NotBeEmpty();
    }

    [Fact]
    public void GeneratePdf_WithoutIndicators_DoesNotThrow() // P1-R8
        => CreateGenerator().GeneratePdf(TestResults.Create(indicators: [])).Should().NotBeEmpty();

    [Fact]
    public void EquityChart_IsAPngImage() // P1-R1, P1-R2
    {
        var png = ChartRenderer.Equity(TestResults.Create());

        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    }

    [Fact]
    public void GeneratePdf_PutsTheChartsOnPage2AndTheTradeLogAfter() // P1-R2, P1-R3, P1-R4
        => PageCount(CreateGenerator().GeneratePdf(TestResults.Create(tradeCount: 3))).Should().Be(3);

    [Fact]
    public void GeneratePdf_ThousandTrades_SpreadsTheTradeLogOverSeveralPages() // P1-R4: nothing truncated
        => PageCount(CreateGenerator().GeneratePdf(TestResults.Create(barCount: 500, tradeCount: 1000))).Should().BeGreaterThan(10);

    [Fact]
    public void Charts_AreRenderedWithEmptyOrPartialInputs() // P1-R3, P1-R8
    {
        var tiny = TestResults.Create(barCount: 2, tradeCount: 0, indicators: []);
        var partial = TestResults.Create(barCount: 2, tradeCount: 1, indicators:
        [
            new IndicatorSeries("sma-50", "SMA 50", IndicatorPane.Price, [null, null], []),
            new IndicatorSeries("short", "Too short", IndicatorPane.Price, [1.0], []),
        ]);
        var empty = TestResults.Create(barCount: 2) with { Bars = [], Equity = [], Trades = [] };

        foreach (var result in new[] { tiny, partial, empty })
        {
            ChartRenderer.Drawdown(result).Should().NotBeEmpty();
            ChartRenderer.Price(result).Should().NotBeEmpty();
            ChartRenderer.Equity(result).Should().NotBeEmpty();
        }
    }

    [Fact]
    public void GeneratePdf_ResultWithoutBars_DoesNotThrow() // P1-R8
        => CreateGenerator().GeneratePdf(TestResults.Create(barCount: 2) with { Bars = [], Equity = [], Trades = [] })
            .Should().NotBeEmpty();

    [Fact]
    public void AddQuantBacktestReports_RegistersASingletonGenerator() // CONTRACT section 2
    {
        using var provider = new ServiceCollection().AddQuantBacktestReports().BuildServiceProvider();

        provider.GetRequiredService<IReportGenerator>().Should().BeOfType<PdfReportGenerator>()
            .And.BeSameAs(provider.GetRequiredService<IReportGenerator>());
    }

    private static int PageCount(byte[] pdf)
        => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)").Count;

    private static IReportGenerator CreateGenerator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(GeneratedAt));
        services.AddQuantBacktestReports();
        return services.BuildServiceProvider().GetRequiredService<IReportGenerator>();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
