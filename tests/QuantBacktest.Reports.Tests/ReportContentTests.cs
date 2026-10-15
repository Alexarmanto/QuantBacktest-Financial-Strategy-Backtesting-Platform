using System.Globalization;
using QuantBacktest.Core.Models;
using QuantBacktest.Reports.Pdf;

namespace QuantBacktest.Reports.Tests;

public class ReportContentTests
{
    [Theory] // P1-R6: percentages with 2 decimals and a sign
    [InlineData(0.1234, "+12.34 %")]
    [InlineData(-0.05, "-5.00 %")]
    [InlineData(0.0, "0.00 %")]
    [InlineData(0.000049, "0.00 %")]
    [InlineData(2.5, "+250.00 %")]
    public void SignedPercent_HasTwoDecimalsAndASign(double fraction, string expected)
        => ReportFormat.SignedPercent(fraction).Should().Be(expected);

    [Fact]
    public void Formats_IgnoreTheCultureOfTheMachine() // P1-R1, P1-R6: en-US whatever the current culture
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            ReportFormat.Money(1_234_567.891m).Should().Be("1,234,567.89");
            ReportFormat.SignedPercent(0.1234).Should().Be("+12.34 %");
            ReportFormat.Ratio(1.2345).Should().Be("1.23");
            ReportFormat.Ratio(-0.004).Should().Be("-0.00");
            ReportFormat.Percent(0.001m).Should().Be("0.10 %");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Ratio_Undefined_ReadsNotAvailable() // P1-R2, CONTRACT section 3
        => ReportFormat.Ratio(null).Should().Be("n/a");

    [Fact]
    public void Drawdown_IsShownAsALoss() // P1-R6
        => ReportFormat.Drawdown(0.2345).Should().Be("-23.45 %");

    [Fact]
    public void Metrics_ListThe13FieldsAsTheyAre() // P1-R2: no metric is recomputed
    {
        var rows = ReportContent.Metrics(TestResults.Create());

        rows.Should().Equal(
            new MetricRow("Final equity", "12,345.67", "9,500.00"),
            new MetricRow("Cumulative return", "+23.46 %", "-5.00 %"),
            new MetricRow("Annualized return", "+12.34 %", "-2.00 %"),
            new MetricRow("Average daily return", "+0.04 %", "-0.01 %"),
            new MetricRow("Annualized volatility", "18.75 %", "21.00 %"),
            new MetricRow("Sharpe ratio", "1.23", "-0.10"),
            new MetricRow("Sortino ratio", "n/a", "-0.15"),
            new MetricRow("Calmar ratio", "0.99", "n/a"),
            new MetricRow("Max drawdown", "-12.34 %", "-30.00 %"),
            new MetricRow("Trades", "3", "1"),
            new MetricRow("Win rate", "66.67 %", "0.00 %"),
            new MetricRow("Profit factor", "n/a", "n/a"),
            new MetricRow("Exposure", "55.00 %", "100.00 %"));
    }

    [Fact]
    public void Metrics_Weekly_NameTheAverageReturnWeekly() // M4, P1-R2
        => ReportContent.Metrics(TestResults.Create(timeframe: Timeframe.Weekly))[3].Label.Should().Be("Average weekly return");

    [Fact]
    public void Summary_ShowsTheSettingsOfTheBacktest() // P1-R2
    {
        var summary = ReportContent.Summary(TestResults.Create(barCount: 30));

        summary.Should().Equal(
            ("Symbol", "MSFT"),
            ("Strategy", "Moving average crossover (sma-crossover)"),
            ("Parameters", "fast = 20, slow = 50"),
            ("Period", "2026-01-05 to 2026-02-03"),
            ("Timeframe", "Daily"),
            ("Initial capital", "10,000.00"),
            ("Fees", "0.10 % per fill"),
            ("Slippage", "0.00 %"),
            ("Stops", "stop-loss 5.00 %, trailing stop 10.00 %"),
            ("Risk-free rate", "2.00 % per year"));
    }

    [Fact]
    public void Summary_WithoutParametersOrStops_SaysSo() // P1-R2
    {
        var summary = ReportContent.Summary(TestResults.Create(
            parameters: new Dictionary<string, double>(),
            execution: new ExecutionSettings(TakeProfitFraction: 0.25m)));

        summary.Single(item => item.Label == "Parameters").Value.Should().Be("Strategy defaults");
        summary.Single(item => item.Label == "Stops").Value.Should().Be("take-profit 25.00 %");
        ReportContent.Summary(TestResults.Create(execution: ExecutionSettings.Default))
            .Single(item => item.Label == "Stops").Value.Should().Be("None");
    }

    [Fact]
    public void Footer_UsesTheGivenDateInUtc() // P1-R5
    {
        var generatedAt = new DateTimeOffset(2026, 10, 16, 1, 30, 0, TimeSpan.FromHours(2));

        ReportContent.Footer(generatedAt).Should().Be("Generated on 2026-10-15 23:30 UTC by QuantBacktest — Market data: Alpha Vantage");
    }

    [Fact]
    public void Trades_ShowEveryFieldOfEveryTrade() // P1-R4
    {
        var result = TestResults.Create(tradeCount: 0) with
        {
            Trades =
            [
                new(new DateOnly(2026, 1, 5), 1234.5m, new DateOnly(2026, 1, 9), 1300.25m, 7.123456m, 18.53m, 449.97m, 0.0517, ExitReason.TrailingStop, 4),
            ],
        };

        var rows = ReportContent.Trades(result);

        ReportContent.TradeColumns.Should().HaveCount(11);
        rows.Should().ContainSingle().Which.Should().Equal(
            "1", "2026-01-05", "1,234.50", "2026-01-09", "1,300.25", "7.1235", "18.53", "449.97", "+5.17 %", "Trailing stop", "4");
    }

    [Fact]
    public void Trades_KeepTheOrderAndTheCountOfTheResult() // P1-R4: no truncation
    {
        var result = TestResults.Create(barCount: 500, tradeCount: 1000);

        var rows = ReportContent.Trades(result);

        rows.Should().HaveCount(1000);
        rows.Select(row => row[1]).Should().Equal(result.Trades.Select(trade => ReportFormat.Date(trade.EntryDate)));
    }

    [Theory] // P1-R4: the exit reason is printed
    [InlineData(ExitReason.Signal, "Signal")]
    [InlineData(ExitReason.StopLoss, "Stop-loss")]
    [InlineData(ExitReason.TakeProfit, "Take-profit")]
    [InlineData(ExitReason.TrailingStop, "Trailing stop")]
    [InlineData(ExitReason.EndOfData, "End of data")]
    public void ExitReasonLabel_IsReadable(ExitReason reason, string expected)
        => ReportContent.ExitReasonLabel(reason).Should().Be(expected);
}
