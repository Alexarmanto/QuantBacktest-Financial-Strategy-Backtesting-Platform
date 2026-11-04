using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class MonteCarloViewModelTests
{
    private const string Backtest =
        "symbol=AAPL&tf=weekly&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0&p.fast=20&p.slow=50";

    private static readonly StubStrategy Crossover = new()
    {
        Id = "sma-crossover",
        Name = "Moving average crossover",
        Parameters =
        [
            new ParameterDefinition("fast", "Fast period", ParameterType.Integer, 20, 2, 200, 1),
            new ParameterDefinition("slow", "Slow period", ParameterType.Integer, 50, 5, 400, 1),
        ],
    };

    private static readonly PerformanceMetrics Metrics = new(10371.39m, 0.0371, 0.0185, 0.0002, 0.15, 1.2, 2.3, 3.4, 0.12, 6, 0.5, 1.75, 0.6);

    private static BacktestQuery Query(string queryString = Backtest)
    {
        BacktestQuery.TryParse(new QueryCollection(QueryHelpers.ParseQuery(queryString)), out var query, out _).Should().BeTrue();
        return query!;
    }

    private static BacktestResult Result() =>
        new(
            new BacktestRequest("AAPL", Timeframe.Weekly, "sma-crossover", new Dictionary<string, double>(), null, null, ExecutionSettings.Default),
            "Moving average crossover",
            false,
            [new PriceBar(new DateOnly(2010, 1, 8), 1, 1, 1, 1, 0), new PriceBar(new DateOnly(2024, 6, 28), 1, 1, 1, 1, 0)],
            [],
            [],
            Metrics,
            Metrics,
            []);

    private static MonteCarloResult Simulation(double probabilityOfLoss = 0.1834, double actualFinalReturn = 0.4567) =>
        new(
            new MonteCarloRequest(2500, -7),
            12,
            new PercentileSummary(-0.1234, 0.05, 0.2, 0.35, 0.6),
            new PercentileSummary(0.08, 0.12, 0.18, 0.25, 0.41),
            probabilityOfLoss,
            actualFinalReturn,
            0.2109,
            [new HistogramBin(-0.2, 0.7, 2500)],
            [new HistogramBin(0.0, 0.5, 2500)]);

    private static MonteCarloViewModel Page(MonteCarloResult? simulation = null) =>
        MonteCarloViewModel.Create(Result(), Crossover, Query(), simulation ?? Simulation(), "http://x/mc", pathBase: "");

    [Fact] // MC4: the five percentiles of both distributions, as returned by Core, formatted (P4-U14)
    public void Percentiles_AreTheFiveOfCore_Formatted()
    {
        Page().Results!.Percentiles.Should().Equal(
            new PercentileRow("5th percentile", "-12.34%", ValueTone.Loss, "8.00%"),
            new PercentileRow("25th percentile", "5.00%", ValueTone.Gain, "12.00%"),
            new PercentileRow("50th percentile (median)", "20.00%", ValueTone.Gain, "18.00%"),
            new PercentileRow("75th percentile", "35.00%", ValueTone.Gain, "25.00%"),
            new PercentileRow("95th percentile", "60.00%", ValueTone.Gain, "41.00%"));
    }

    [Fact] // MC5, P4-X4: probability of loss and actual values, plus the echo of the request (sims, seed, trades)
    public void Summary_ShowsTheRequestAndTheActualValues()
    {
        Page().Results!.Summary.Should().Equal(
            new MonteCarloSummaryRow("Simulations", "2,500"),
            new MonteCarloSummaryRow("Seed", "-7"),
            new MonteCarloSummaryRow("Trades resampled", "12"),
            new MonteCarloSummaryRow("Probability of loss", "18.34%"),
            new MonteCarloSummaryRow(MonteCarloViewModel.ActualFinalReturnLabel, "45.67%", ValueTone.Gain),
            new MonteCarloSummaryRow(MonteCarloViewModel.ActualMaxDrawdownLabel, "21.09%"));
    }

    [Fact] // 2: drawdowns positive, the convention of "Max drawdown" in the Performance table of Result (no sign change)
    public void Drawdowns_ArePositive_LikeTheResultTable()
    {
        var results = Page().Results!;

        results.Percentiles.Select(r => r.MaxDrawdown).Should().OnlyContain(text => !text.StartsWith('-'));
        results.Summary.Single(r => r.Label == MonteCarloViewModel.ActualMaxDrawdownLabel).Value.Should().Be("21.09%");
    }

    [Fact] // MC7: the page always states that the Monte Carlo drawdown is trade by trade
    public void Mc7Warning_NamesTheTradeByTradeDrawdown()
    {
        MonteCarloViewModel.Mc7Warning.Should().Contain("trade by trade").And.Contain("not comparable");
        MonteCarloChartsViewModel.MaxDrawdownTitle.Should().Contain("trade by trade");
    }

    [Fact] // P4-X4, decisions H1/H2: back to Result, PDF with mc=true, form with the effective values; header reused
    public void Links_Form_AndHeader()
    {
        var page = MonteCarloViewModel.Create(Result(), Crossover, Query(), Simulation(), "http://x/mc", pathBase: "/app");

        page.ResultUrl.Should().Be("/app/Backtest/Result?" + Backtest);
        page.Results!.PdfUrl.Should().Be("/app/Backtest/Report.pdf?" + Backtest + "&mc=true&sims=2500&seed=-7");
        page.Form.Action.Should().Be("/app/Backtest/MonteCarlo");
        page.Form.Hidden.Should().Equal(Query().ToQueryPairs());
        page.Form.Fields.Select(f => (f.Setting.Key, f.Value, f.Error)).Should().Equal(("sims", "2500", null), ("seed", "-7", null));
        page.Header!.Period.Should().Be("2010-01-08 to 2024-06-28");
        page.Header.UnadjustedWarning.Should().Be(ResultHeaderViewModel.UnadjustedPricesWarning);
        page.Header.Permalink.Should().Be("http://x/mc");
        page.Alert.Should().BeNull();
    }

    [Fact] // MC1: a refused simulation keeps the header and the form, shows the message, and has no results
    public void Refused_ShowsTheMessage()
    {
        var page = MonteCarloViewModel.Refused(
            Result(), Crossover, Query(), new MonteCarloRequest(), new BacktestValidationException("Too few trades."), "http://x/mc", "");

        page.Alert!.Message.Should().Be("Too few trades.");
        page.Results.Should().BeNull();
        page.Header.Should().NotBeNull();
        page.Form.Fields.Select(f => f.Value).Should().Equal("1000", "42");
    }

    [Fact] // decision H7: invalid settings → no header (no period: nothing ran), the raw input and its error
    public void InvalidSettings_KeepsTheRawInput_WithoutHeader()
    {
        var received = new QueryCollection(QueryHelpers.ParseQuery(Backtest + "&sims=50&seed=8"));
        var errors = new[] { new QueryFieldError("sims", "Bad sims.") };

        var page = MonteCarloViewModel.InvalidSettings(Crossover, Query(), received, errors, "");

        page.Header.Should().BeNull();
        page.Results.Should().BeNull();
        page.Alert.Should().BeNull();
        page.StrategyName.Should().Be("Moving average crossover");
        page.Timeframe.Should().Be("Weekly");
        page.Form.Fields.Select(f => (f.Value, f.Error)).Should().Equal(("50", "Bad sims."), ("8", null));
    }

    [Fact] // P4-U14: non-finite values print "n/a", never "NaN%"
    public void NonFiniteValues_AreNotAvailable()
    {
        var summary = Page(Simulation(probabilityOfLoss: double.NaN, actualFinalReturn: double.PositiveInfinity)).Results!.Summary;

        summary.Single(r => r.Label == "Probability of loss").Value.Should().Be("n/a");
        summary.Single(r => r.Label == MonteCarloViewModel.ActualFinalReturnLabel).Should().Be(
            new MonteCarloSummaryRow(MonteCarloViewModel.ActualFinalReturnLabel, "n/a", ValueTone.Neutral));
    }

    [Fact] // MC1 bounds and the whole-number step are written on the inputs (TagBuilder, no empty data-*)
    public void Inputs_CarryTheContractRange()
    {
        var html = Render(MonteCarloViewModel.SimulationsSetting.Input("1000", invalid: false));

        html.Should().Contain("min=\"100\"").And.Contain("max=\"10000\"").And.Contain("step=\"1\"").And.NotContain("data-exclusive-min");
    }

    private static string Render(Microsoft.AspNetCore.Html.IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }
}
