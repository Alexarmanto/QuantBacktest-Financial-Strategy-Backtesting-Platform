using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Reports.Charts;
using QuantBacktest.Reports.Pdf;

namespace QuantBacktest.Reports.Tests;

public class AdvancedReportTests
{
    private static readonly MonteCarloResult MonteCarlo = new(
        new MonteCarloRequest(1000, 42),
        12,
        new PercentileSummary(-0.1234, -0.02, 0.05, 0.111, 0.25),
        new PercentileSummary(0.03, 0.08, 0.12, 0.18, 0.3),
        ProbabilityOfLoss: 0.183,
        ActualFinalReturn: 0.0612,
        ActualMaxDrawdown: 0.1,
        [new HistogramBin(-0.2, 0.0, 300), new HistogramBin(0.0, 0.2, 600), new HistogramBin(0.2, 0.4, 100)],
        [new HistogramBin(0.0, 0.15, 700), new HistogramBin(0.15, 0.3, 300)]);

    [Fact]
    public void MonteCarloPercentiles_ShowP5ToP95AsGiven() // P1-X1
        => ReportContent.MonteCarloPercentiles(MonteCarlo).Should().Equal(
            new MetricRow("P5", "-12.34 %", "-3.00 %"),
            new MetricRow("P25", "-2.00 %", "-8.00 %"),
            new MetricRow("P50 (median)", "+5.00 %", "-12.00 %"),
            new MetricRow("P75", "+11.10 %", "-18.00 %"),
            new MetricRow("P95", "+25.00 %", "-30.00 %"));

    [Fact]
    public void MonteCarloSummary_ShowsProbabilityOfLossAndActualValues() // P1-X1
        => ReportContent.MonteCarloSummary(MonteCarlo).Should().Equal(
            ("Simulations", "1,000"),
            ("Seed", "42"),
            ("Trades resampled", "12"),
            ("Probability of loss", "18.30 %"),
            ("Actual final return", "+6.12 %"),
            ("Actual max drawdown", "-10.00 %"));

    [Fact]
    public void MonteCarloDrawdownNote_IsTheSentenceOfTheNotice() // P1-X1, MC7
        => ReportContent.MonteCarloDrawdownNote.Should().Be("Trade-level drawdowns are not comparable with the bar-level maximum drawdown.");

    [Fact]
    public void GeneratePdf_WithMonteCarlo_AddsOnePage() // P1-X1
    {
        var generator = CreateGenerator();
        var result = TestResults.Create();

        var without = PageCount(generator.GeneratePdf(result));
        var with = PageCount(generator.GeneratePdf(result, MonteCarlo));

        with.Should().Be(without + 1);
    }

    [Theory] // P1-X1, P1-R8: with and without Monte Carlo, with large results, never an exception
    [InlineData(2, 0)]
    [InlineData(500, 1000)]
    public void GeneratePdf_WithMonteCarlo_DoesNotThrow(int barCount, int tradeCount)
        => CreateGenerator().GeneratePdf(TestResults.Create(barCount, tradeCount), MonteCarlo).Should().NotBeEmpty();

    [Fact]
    public void GeneratePdf_WithFakeMonteCarloOfAFakeBacktest_DoesNotThrow() // P1-X1
    {
        var result = TestResults.Create(tradeCount: 20);
        var monteCarlo = new FakeMonteCarloService().Run(result, new MonteCarloRequest());

        CreateGenerator().GeneratePdf(result, monteCarlo).Should().NotBeEmpty();
    }

    [Fact]
    public void Histogram_SingleOrNoBin_IsRendered() // MC6: min = max gives one bin; P1-R8
    {
        ChartRenderer.Histogram([new HistogramBin(0.05, 0.05, 1000)], "One bin", asLoss: false).Should().NotBeEmpty();
        ChartRenderer.Histogram([], "No bin", asLoss: true).Should().NotBeEmpty();
    }

    [Fact]
    public void Oscillators_OnlyWhenTheResultHasOscillatorIndicators() // P1-X1
    {
        var withRsi = TestResults.Create();
        var priceOnly = TestResults.Create(indicators: [TestResults.Indicator(60, "sma-20", "SMA 20", IndicatorPane.Price)]);

        ChartRenderer.Oscillators(withRsi).Should().NotBeNull().And.NotBeEmpty();
        ChartRenderer.Oscillators(priceOnly).Should().BeNull();
    }

    [Fact]
    public async Task CustomStrategy_RulesComeFromTheCatalog() // P1-X1
    {
        var generator = CreateGenerator(withCatalog: true);
        var expected = (await BuildFakeCatalog().FindAsync(FakeCustomStrategyStore.Example.Id))!.Documentation;

        generator.FindCustomRules(FakeCustomStrategyStore.Example.Id).Should().Be(expected);
        generator.FindCustomRules("fake-threshold").Should().BeNull("built-in strategies have no rules to print");
        generator.FindCustomRules("custom-unknown").Should().BeNull();
        CreateGenerator().FindCustomRules(FakeCustomStrategyStore.Example.Id).Should().BeNull("without a catalog the rules are left out");
    }

    [Fact]
    public void GeneratePdf_ForACustomStrategy_DoesNotThrow() // P1-X1
    {
        var result = TestResults.Create() with
        {
            Request = TestResults.Create().Request with { StrategyId = FakeCustomStrategyStore.Example.Id },
        };

        CreateGenerator(withCatalog: true).GeneratePdf(result, MonteCarlo).Should().NotBeEmpty();
    }

    private static int PageCount(byte[] pdf)
        => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)").Count;

    private static IStrategyCatalog BuildFakeCatalog()
        => new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());

    private static PdfReportGenerator CreateGenerator(bool withCatalog = false)
    {
        var services = new ServiceCollection();
        if (withCatalog)
        {
            services.AddSingleton(BuildFakeCatalog());
        }

        services.AddQuantBacktestReports();
        return (PdfReportGenerator)services.BuildServiceProvider().GetRequiredService<IReportGenerator>();
    }
}
