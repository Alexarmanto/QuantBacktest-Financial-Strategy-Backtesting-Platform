using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

/// <summary>GET /Backtest/MonteCarlo (P4-X4), with the Fakes and the recording doubles; no Mvc.Testing.</summary>
public class MonteCarloControllerTests
{
    private const string MonteCarloPath = "/Backtest/MonteCarlo";

    /// <summary>fake-threshold supports daily and weekly; lookback int 10 [1, 100], threshold dec 0.02 [0, 0.5].</summary>
    private const string Backtest =
        "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02";

    private const string Canonical = Backtest + "&sims=1000&seed=42";

    private sealed record Run(
        IActionResult Result,
        CountingBacktestService Backtests,
        RecordingMonteCarloService MonteCarlo);

    private static async Task<Run> MonteCarloAsync(
        string queryString,
        int? keepTrades = null,
        IBacktestService? backtests = null,
        RecordingMonteCarloService? monteCarlo = null)
    {
        var catalog = new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());
        var marketData = new FakeMarketDataService();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost:5080");
        httpContext.Request.Path = MonteCarloPath;
        httpContext.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString("?" + queryString);
        var controller = new BacktestController(catalog, marketData, NullLogger<BacktestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var counting = new CountingBacktestService(backtests ?? new FakeBacktestService(marketData, catalog), keepTrades);
        monteCarlo ??= new RecordingMonteCarloService();
        var result = await controller.MonteCarlo(counting, monteCarlo, CancellationToken.None);
        return new Run(result, counting, monteCarlo);
    }

    private static MonteCarloViewModel Page(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().BeNull();
        return view.Model.Should().BeOfType<MonteCarloViewModel>().Subject;
    }

    private static BacktestFormViewModel BacktestForm(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Index");
        return view.Model.Should().BeOfType<BacktestFormViewModel>().Subject;
    }

    [Fact] // PART-4 section 5 (routes): GET /Backtest/MonteCarlo, conventional route of BacktestController
    public void MonteCarlo_IsAnHttpGetAction()
    {
        var method = typeof(BacktestController).GetMethod(nameof(BacktestController.MonteCarlo));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
    }

    [Fact] // same rule as Result: no settings at all → the form
    public async Task MonteCarlo_WithoutParameters_RedirectsToTheForm()
    {
        var run = await MonteCarloAsync("");

        run.Result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(BacktestController.Index));
        run.Backtests.Calls.Should().Be(0);
    }

    [Theory] // P4-X4 + decisions H1/(a): tolerant read, then one redirect to the canonical URL; nothing is run before
    [InlineData(Backtest, Canonical)]
    [InlineData("symbol=aapl&strategy=fake-threshold", Canonical)]
    [InlineData(Backtest + "&mc=true&sims=1000&seed=42", Canonical)]
    [InlineData(Backtest + "&seed=7&sims=500", Backtest + "&sims=500&seed=7")]
    [InlineData(Backtest + "&sims=&seed=", Canonical)]
    public async Task MonteCarlo_NonCanonicalUrl_RedirectsOnce(string received, string canonical)
    {
        var run = await MonteCarloAsync(received);

        run.Result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(MonteCarloPath + "?" + canonical);
        run.Backtests.Calls.Should().Be(0);
        run.MonteCarlo.Calls.Should().BeEmpty();
    }

    [Fact] // P4-X4: canonical → 1 backtest, 1 simulation, the page with every block
    public async Task MonteCarlo_CanonicalUrl_ShowsTheResults()
    {
        var run = await MonteCarloAsync(Canonical);

        var page = Page(run.Result);
        run.Backtests.Calls.Should().Be(1);
        run.MonteCarlo.Calls.Should().ContainSingle();
        page.Alert.Should().BeNull();
        page.Header.Should().NotBeNull();
        page.Header!.Permalink.Should().Be("http://localhost:5080" + MonteCarloPath + "?" + Canonical);
        page.ResultUrl.Should().Be("/Backtest/Result?" + Backtest);
        page.Results.Should().NotBeNull();
        page.Results!.Percentiles.Should().HaveCount(5);
        page.Results.PdfUrl.Should().Be("/Backtest/Report.pdf?" + Backtest + "&mc=true&sims=1000&seed=42");
        page.Form.Fields.Select(f => f.Value).Should().Equal("1000", "42");
    }

    [Fact] // P4-X4, decision H5: sims and seed of the URL reach IMonteCarloService unchanged, with the backtest result
    public async Task MonteCarlo_PassesSimsAndSeed_ToTheService()
    {
        var run = await MonteCarloAsync(Backtest + "&sims=2500&seed=-3");

        Page(run.Result);
        var call = run.MonteCarlo.Calls.Should().ContainSingle().Subject;
        call.Request.Should().Be(new MonteCarloRequest(2500, -3));
        call.Result.Request.Symbol.Should().Be("AAPL");
    }

    [Theory] // decision H7: invalid sims / seed → this page with the field error and the raw input; nothing is run
    [InlineData(Backtest + "&sims=99&seed=42", "sims", "99", "Simulations must be a whole number between 100 and 10000.")]
    [InlineData(Backtest + "&sims=1000&seed=abc", "seed", "abc", "Seed must be a whole number between -2147483648 and 2147483647.")]
    public async Task MonteCarlo_InvalidSettings_ShowsTheError_WithoutRunningTheBacktest(
        string queryString, string field, string rawValue, string message)
    {
        var run = await MonteCarloAsync(queryString);

        var page = Page(run.Result);
        run.Backtests.Calls.Should().Be(0);
        run.MonteCarlo.Calls.Should().BeEmpty();
        page.Header.Should().BeNull();
        page.Results.Should().BeNull();
        page.Symbol.Should().Be("AAPL");
        page.ResultUrl.Should().Be("/Backtest/Result?" + Backtest);
        var input = page.Form.Fields.Single(f => f.Setting.Key == field);
        input.Value.Should().Be(rawValue);
        input.Error.Should().Be(message);
    }

    [Fact] // same as Result: an invalid backtest setting shows the backtest form with the error
    public async Task MonteCarlo_InvalidBacktestInput_ShowsTheBacktestForm()
    {
        var run = await MonteCarloAsync("symbol=AAPL&strategy=fake-threshold&capital=-5&sims=1000&seed=42");

        BacktestForm(run.Result).Error("capital").Should().Be("Initial capital must be greater than 0.");
        run.Backtests.Calls.Should().Be(0);
    }

    [Fact] // PART-4 section 7, P4-X4, MC1: fewer than 5 trades → the message of the BacktestValidationException
    public async Task MonteCarlo_FewerThanFiveTrades_ShowsTheMessage()
    {
        var run = await MonteCarloAsync(Canonical, keepTrades: 3);

        var page = Page(run.Result);
        page.Alert!.Message.Should().Be("Monte Carlo needs at least 5 trades; this backtest has 3.");
        page.Results.Should().BeNull();
        page.Header.Should().NotBeNull();
    }

    [Fact] // P4-U12: a refused backtest (MarketDataException) is shown like on Result, with its hint
    public async Task MonteCarlo_RateLimit_ShowsTheBacktestFormAlert()
    {
        var run = await MonteCarloAsync(Canonical.Replace("symbol=AAPL", "symbol=LIMIT", StringComparison.Ordinal));

        var form = BacktestForm(run.Result);
        form.Alert!.Hint.Should().Be(FormAlert.RateLimitHint);
        run.MonteCarlo.Calls.Should().BeEmpty();
    }

    [Fact] // P4-U12: an unexpected exception of the backtest goes to /Home/Error (not caught here)
    public async Task MonteCarlo_UnexpectedBacktestException_IsNotSwallowed()
    {
        var act = () => MonteCarloAsync(Canonical, backtests: new ThrowingBacktestService());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] // P4-U12: IMonteCarloService only declares BacktestValidationException; anything else is a bug
    public async Task MonteCarlo_UnexpectedMonteCarloException_IsNotSwallowed()
    {
        var act = () => MonteCarloAsync(
            Canonical,
            monteCarlo: new RecordingMonteCarloService(new InvalidOperationException("Bug.")));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ThrowingBacktestService : IBacktestService
    {
        public Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bug.");
    }
}
