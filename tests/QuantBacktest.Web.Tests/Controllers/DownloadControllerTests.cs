using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

/// <summary>
/// GET /Backtest/Report.pdf and GET /Backtest/Trades.csv (PART-4 sections 5 and 7, docs/CONTRACT.md sections 11
/// and 12), with the Fakes and the recording doubles; no Mvc.Testing.
/// </summary>
public class DownloadControllerTests
{
    private const string ReportPath = "/Backtest/Report.pdf";
    private const string TradesCsvPath = "/Backtest/Trades.csv";
    private const string MonteCarloPath = "/Backtest/MonteCarlo";

    /// <summary>
    /// fake-threshold supports daily and weekly; lookback int 10 [1, 100], threshold dec 0.02 [0, 0.5]. Fake daily
    /// bars are every weekday from 2018-01-01, so 2020-12-31 (a Thursday) is the last bar; 6 trades in 2020.
    /// </summary>
    private const string Backtest =
        "symbol=AAPL&tf=daily&strategy=fake-threshold&from=2020-01-01&to=2020-12-31"
        + "&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02";

    private const string WithMc = Backtest + "&mc=true&sims=1000&seed=42";

    private const string PdfName = "QuantBacktest_AAPL_fake-threshold_20201231.pdf";
    private const string CsvName = "QuantBacktest_AAPL_fake-threshold_20201231.csv";

    private const string CsvHeader =
        "entry_date,entry_price,exit_date,exit_price,quantity,fees,net_profit,return_pct,exit_reason,holding_bars";

    private sealed record Run(
        IActionResult Result,
        CountingBacktestService Backtests,
        RecordingMonteCarloService MonteCarlo,
        RecordingReportGenerator Reports);

    private static BacktestController Controller(string path, string queryString, FakeStrategyCatalog catalog, FakeMarketDataService marketData)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost:5080");
        httpContext.Request.Path = path;
        httpContext.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString("?" + queryString);
        return new BacktestController(catalog, marketData, NullLogger<BacktestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static async Task<Run> ReportAsync(
        string queryString,
        int? keepTrades = null,
        IBacktestService? backtests = null,
        RecordingMonteCarloService? monteCarlo = null,
        RecordingReportGenerator? reports = null)
    {
        var catalog = new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());
        var marketData = new FakeMarketDataService();
        var controller = Controller(ReportPath, queryString, catalog, marketData);
        var counting = new CountingBacktestService(backtests ?? new FakeBacktestService(marketData, catalog), keepTrades);
        monteCarlo ??= new RecordingMonteCarloService();
        reports ??= new RecordingReportGenerator();
        var result = await controller.Report(counting, monteCarlo, reports, CancellationToken.None);
        return new Run(result, counting, monteCarlo, reports);
    }

    private static async Task<Run> TradesCsvAsync(
        string queryString,
        int? keepTrades = null,
        IBacktestService? backtests = null,
        RecordingReportGenerator? reports = null)
    {
        var catalog = new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());
        var marketData = new FakeMarketDataService();
        var controller = Controller(TradesCsvPath, queryString, catalog, marketData);
        var counting = new CountingBacktestService(backtests ?? new FakeBacktestService(marketData, catalog), keepTrades);
        reports ??= new RecordingReportGenerator();
        var result = await controller.TradesCsv(counting, reports, CancellationToken.None);

        // TradesCsv receives no IMonteCarloService (TradesCsv_DoesNotDependOnMonteCarlo): this one stays unused.
        return new Run(result, counting, new RecordingMonteCarloService(), reports);
    }

    private static FileContentResult Downloaded(IActionResult result) => result.Should().BeOfType<FileContentResult>().Subject;

    private static BacktestFormViewModel BacktestForm(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Index");
        return view.Model.Should().BeOfType<BacktestFormViewModel>().Subject;
    }

    /// <summary>The Monte Carlo page rendered by Report: the view must be named, the current action is Report.</summary>
    private static MonteCarloViewModel MonteCarloPage(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be(nameof(BacktestController.MonteCarlo));
        return view.Model.Should().BeOfType<MonteCarloViewModel>().Subject;
    }

    // ---- Routes ----

    [Fact] // PART-4 section 5 (routes): GET /Backtest/Report.pdf
    public void Report_IsAnHttpGetAction_OnBacktestReportPdf()
    {
        var method = typeof(BacktestController).GetMethod(nameof(BacktestController.Report));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be(ReportPath);
        method.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
    }

    [Fact] // PART-4 section 5 (routes): GET /Backtest/Trades.csv
    public void TradesCsv_IsAnHttpGetAction_OnBacktestTradesCsv()
    {
        var method = typeof(BacktestController).GetMethod(nameof(BacktestController.TradesCsv));

        method.Should().NotBeNull();
        method!.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be(TradesCsvPath);
        method.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
    }

    [Fact] // PART-4 section 5: the CSV has no Monte Carlo; the action cannot even reach the service
    public void TradesCsv_DoesNotDependOnMonteCarlo()
    {
        var method = typeof(BacktestController).GetMethod(nameof(BacktestController.TradesCsv))!;

        method.GetParameters().Select(p => p.ParameterType).Should().NotContain(typeof(IMonteCarloService));
    }

    [Fact] // decision A, as Result: no settings at all → the form
    public async Task Report_WithoutParameters_RedirectsToTheForm()
    {
        var run = await ReportAsync("");

        run.Result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(BacktestController.Index));
        run.Backtests.Calls.Should().Be(0);
        run.Reports.PdfCalls.Should().BeEmpty();
    }

    [Fact] // decision A, as Result: no settings at all → the form
    public async Task TradesCsv_WithoutParameters_RedirectsToTheForm()
    {
        var run = await TradesCsvAsync("");

        run.Result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(BacktestController.Index));
        run.Backtests.Calls.Should().Be(0);
        run.Reports.CsvCalls.Should().BeEmpty();
    }

    // ---- PDF ----

    [Fact] // PART-4 section 7 (downloads), CONTRACT section 11, P1: application/pdf, contract name, the generator's bytes
    public async Task Report_CanonicalUrl_ReturnsThePdf_WithTheContractFileName()
    {
        var run = await ReportAsync(Backtest);

        var file = Downloaded(run.Result);
        file.ContentType.Should().Be("application/pdf");
        file.FileDownloadName.Should().Be(PdfName);
        file.FileContents.Should().Equal(run.Reports.PdfCalls.Single().Pdf);
    }

    [Fact] // P1: without mc, GeneratePdf(result, null) once, and Monte Carlo is never run
    public async Task Report_WithoutMc_CallsGeneratePdfWithNullMonteCarlo_AndNeverRunsMonteCarlo()
    {
        var run = await ReportAsync(Backtest);

        Downloaded(run.Result);
        run.Backtests.Calls.Should().Be(1);
        run.MonteCarlo.Calls.Should().BeEmpty();
        var call = run.Reports.PdfCalls.Should().ContainSingle().Subject;
        call.MonteCarlo.Should().BeNull();
        call.Result.Request.Symbol.Should().Be("AAPL");
    }

    [Fact] // P2, decision H5: sims and seed reach IMonteCarloService unchanged; its result reaches GeneratePdf
    public async Task Report_WithMc_PassesSimsAndSeed_AndTheSimulationToGeneratePdf()
    {
        var monteCarlo = new RecordingMonteCarloService();
        var run = await ReportAsync(Backtest + "&mc=true&sims=2500&seed=-7", monteCarlo: monteCarlo);

        Downloaded(run.Result).ContentType.Should().Be("application/pdf");
        run.Backtests.Calls.Should().Be(1);
        var simulation = monteCarlo.Calls.Should().ContainSingle().Subject;
        simulation.Request.Should().Be(new MonteCarloRequest(2500, -7));
        var pdf = run.Reports.PdfCalls.Should().ContainSingle().Subject;
        pdf.Result.Should().BeSameAs(simulation.Result);
        pdf.MonteCarlo.Should().NotBeNull();
        pdf.MonteCarlo!.Request.Should().Be(new MonteCarloRequest(2500, -7));
    }

    [Fact] // CONTRACT section 11, D6: the date is the last bar of the period, not "to" nor the clock
    public async Task Report_FileName_UsesTheLastBarOfThePeriod()
    {
        // 2021-01-03 is a Sunday: the last fake daily bar of the period is Friday 2021-01-01.
        var run = await ReportAsync(Backtest.Replace("to=2020-12-31", "to=2021-01-03", StringComparison.Ordinal));

        Downloaded(run.Result).FileDownloadName.Should().Be("QuantBacktest_AAPL_fake-threshold_20210101.pdf");
    }

    [Fact] // CONTRACT section 11 defines a single PDF name: the Monte Carlo section does not change it
    public async Task Report_FileName_IsTheSameWithAndWithoutMc()
    {
        var without = await ReportAsync(Backtest);
        var with = await ReportAsync(WithMc);

        Downloaded(with.Result).FileDownloadName.Should().Be(Downloaded(without.Result).FileDownloadName).And.Be(PdfName);
    }

    [Theory] // decisions D1, D4: tolerant read, then one redirect to the canonical URL; nothing is run before
    [InlineData("symbol=aapl&strategy=fake-threshold&from=2020-01-01&to=2020-12-31", Backtest)]
    [InlineData(Backtest + "&sims=1000&seed=42", Backtest)]
    [InlineData(Backtest + "&mc=", Backtest)]
    [InlineData(Backtest + "&mc=TRUE&sims=1000&seed=42", WithMc)]
    [InlineData(Backtest + "&mc=true", WithMc)]
    [InlineData(Backtest + "&sims=500&seed=7&mc=true", Backtest + "&mc=true&sims=500&seed=7")]
    public async Task Report_NonCanonicalUrl_RedirectsOnce(string received, string canonical)
    {
        var run = await ReportAsync(received);

        run.Result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(ReportPath + "?" + canonical);
        run.Backtests.Calls.Should().Be(0);
        run.MonteCarlo.Calls.Should().BeEmpty();
        run.Reports.PdfCalls.Should().BeEmpty();
    }

    [Fact] // P4-U12, PART-4 section 7: invalid input → the form with the error; no PDF
    public async Task Report_InvalidBacktestInput_ShowsTheBacktestForm_AndNeverCallsTheGenerator()
    {
        var run = await ReportAsync("symbol=AAPL&strategy=fake-threshold&capital=-5");

        BacktestForm(run.Result).Error("capital").Should().Be("Initial capital must be greater than 0.");
        run.Backtests.Calls.Should().Be(0);
        run.Reports.PdfCalls.Should().BeEmpty();
    }

    [Theory] // decision D1: mc other than "true" (or repeated) → the backtest form, error listed at the top (no mc input)
    [InlineData(Backtest + "&mc=false&sims=1000&seed=42", BacktestQuery.MonteCarloFlagMessage)]
    [InlineData(Backtest + "&mc=1", BacktestQuery.MonteCarloFlagMessage)]
    [InlineData(Backtest + "&mc=true&mc=true", "Monte Carlo flag (mc) is given more than once.")]
    public async Task Report_InvalidMcValue_ShowsTheBacktestFormError(string queryString, string message)
    {
        var run = await ReportAsync(queryString);

        BacktestForm(run.Result).OtherErrors.Should().Equal(message);
        run.Backtests.Calls.Should().Be(0);
        run.MonteCarlo.Calls.Should().BeEmpty();
        run.Reports.PdfCalls.Should().BeEmpty();
    }

    [Fact] // decision D2 + condition 1: invalid sims → the Monte Carlo page, its form submitting to /Backtest/MonteCarlo
    public async Task Report_WithMc_InvalidSims_ShowsTheMonteCarloPage_WithoutRunningAnything()
    {
        var run = await ReportAsync(Backtest + "&mc=true&sims=50&seed=42");

        var page = MonteCarloPage(run.Result);
        run.Backtests.Calls.Should().Be(0);
        run.MonteCarlo.Calls.Should().BeEmpty();
        run.Reports.PdfCalls.Should().BeEmpty();
        page.Header.Should().BeNull();
        var sims = page.Form.Fields.Single(f => f.Setting.Key == BacktestQuery.SimulationsKey);
        sims.Value.Should().Be("50");
        sims.Error.Should().Be("Simulations must be a whole number between 100 and 10000.");

        // Condition 1: the form goes to the Monte Carlo page (never back to Report.pdf), without mc in its fields.
        page.Form.Action.Should().Be(MonteCarloPath);
        page.Form.Hidden.Select(p => p.Key).Should().NotContain(BacktestQuery.MonteCarloKey);
        string.Join("&", page.Form.Hidden.Select(p => p.Key + "=" + p.Value)).Should().Be(Backtest);

        // Condition 2: no results, so the view does not render "Download PDF with Monte Carlo".
        page.Results.Should().BeNull();
    }

    [Fact] // decision D3, MC1: fewer than 5 trades → the Monte Carlo message, and GeneratePdf is never called
    public async Task Report_WithMc_FewerThanFiveTrades_ShowsTheMonteCarloMessage_AndNeverCallsGeneratePdf()
    {
        var run = await ReportAsync(WithMc, keepTrades: 3);

        var page = MonteCarloPage(run.Result);
        page.Alert!.Message.Should().Be("Monte Carlo needs at least 5 trades; this backtest has 3.");
        run.MonteCarlo.Calls.Should().ContainSingle();
        run.Reports.PdfCalls.Should().BeEmpty();

        // Condition 2: no results, so no "Download PDF with Monte Carlo" that would loop on this page.
        page.Results.Should().BeNull();
        page.Form.Action.Should().Be(MonteCarloPath);
    }

    [Fact] // decision D3: the refused page's Copy link is the Monte Carlo page, not the PDF URL
    public async Task Report_WithMc_RefusedPage_PermalinkPointsToTheMonteCarloPage()
    {
        var run = await ReportAsync(WithMc, keepTrades: 0);

        var page = MonteCarloPage(run.Result);
        page.Header!.Permalink.Should().Be("http://localhost:5080" + MonteCarloPath + "?" + Backtest + "&sims=1000&seed=42");
        page.ResultUrl.Should().Be("/Backtest/Result?" + Backtest);
    }

    [Theory] // P4-U12, PART-4 section 7: UNKNOWN, NOKEY, LIMIT → the form alert with its hints; no PDF, no simulation
    [InlineData("UNKNOWN", "Symbol 'UNKNOWN' was not found.", null, null)]
    [InlineData("NOKEY", "No Alpha Vantage API key is configured.", FormAlert.MissingApiKeyHint, FormAlert.MissingApiKeyCommand)]
    [InlineData("LIMIT", "The daily Alpha Vantage limit (25 requests) is reached.", FormAlert.RateLimitHint, null)]
    public async Task Report_MarketDataErrors_ShowTheFormAlert_AndNeverCallTheGenerator(
        string symbol, string message, string? hint, string? command)
    {
        var run = await ReportAsync(WithMc.Replace("symbol=AAPL", "symbol=" + symbol, StringComparison.Ordinal));

        BacktestForm(run.Result).Alert.Should().Be(new FormAlert(message, hint, command));
        run.MonteCarlo.Calls.Should().BeEmpty();
        run.Reports.PdfCalls.Should().BeEmpty();
    }

    [Fact] // CONTRACT section 12, P4-U12: an unexpected exception of the backtest goes to /Home/Error (not caught)
    public async Task Report_UnexpectedBacktestException_IsNotSwallowed()
    {
        var act = () => ReportAsync(Backtest, backtests: new ThrowingBacktestService());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] // CONTRACT section 12: IReportGenerator declares no exception; anything it throws is a bug
    public async Task Report_UnexpectedGeneratorException_IsNotSwallowed()
    {
        var act = () => ReportAsync(Backtest, reports: new RecordingReportGenerator(new InvalidOperationException("Bug.")));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] // CONTRACT section 12: IMonteCarloService only declares BacktestValidationException
    public async Task Report_WithMc_UnexpectedMonteCarloException_IsNotSwallowed()
    {
        var act = () => ReportAsync(WithMc, monteCarlo: new RecordingMonteCarloService(new InvalidOperationException("Bug.")));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- CSV ----

    [Fact] // PART-4 section 7 (downloads), CONTRACT section 11, D5: exactly text/csv, contract name
    public async Task TradesCsv_CanonicalUrl_ReturnsTheCsv_WithTheContractFileName()
    {
        var run = await TradesCsvAsync(Backtest);

        var file = Downloaded(run.Result);
        file.ContentType.Should().Be("text/csv");
        file.FileDownloadName.Should().Be(CsvName);
        run.Backtests.Calls.Should().Be(1);
    }

    [Fact] // P3: the bytes are the UTF-8 of GenerateTradesCsv, untouched (header of section 11 first)
    public async Task TradesCsv_Bytes_AreTheUtf8OfGenerateTradesCsv()
    {
        var run = await TradesCsvAsync(Backtest);

        var csv = run.Reports.CsvCalls.Should().ContainSingle().Subject.Csv;
        Downloaded(run.Result).FileContents.Should().Equal(System.Text.Encoding.UTF8.GetBytes(csv));
        csv.Should().StartWith(CsvHeader + "\n");
    }

    [Fact] // D5: no byte order mark
    public async Task TradesCsv_HasNoUtf8Bom()
    {
        var run = await TradesCsvAsync(Backtest);

        var bytes = Downloaded(run.Result).FileContents;
        bytes.Length.Should().BeGreaterThanOrEqualTo(3);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact] // CONTRACT section 11: header always present; zero trades is not an error
    public async Task TradesCsv_ZeroTrades_ReturnsTheHeaderOnly()
    {
        var run = await TradesCsvAsync(Backtest, keepTrades: 0);

        Downloaded(run.Result).FileContents.Should().Equal(System.Text.Encoding.UTF8.GetBytes(CsvHeader + "\n"));
    }

    [Fact] // decision D4: mc, sims and seed are not part of the CSV URL → one redirect that drops them; nothing run
    public async Task TradesCsv_WithMonteCarloParameters_RedirectsWithoutThem()
    {
        var run = await TradesCsvAsync(WithMc);

        run.Result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(TradesCsvPath + "?" + Backtest);
        run.Backtests.Calls.Should().Be(0);
        run.Reports.CsvCalls.Should().BeEmpty();
    }

    [Theory] // decision D4: tolerant read, then one redirect to the canonical URL
    [InlineData("symbol=aapl&strategy=fake-threshold&from=2020-01-01&to=2020-12-31")]
    [InlineData("strategy=fake-threshold&symbol=AAPL&from=2020-01-01&to=2020-12-31&p.threshold=0.020&p.lookback=10")]
    public async Task TradesCsv_NonCanonicalUrl_RedirectsOnce(string received)
    {
        var run = await TradesCsvAsync(received);

        run.Result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be(TradesCsvPath + "?" + Backtest);
        run.Backtests.Calls.Should().Be(0);
    }

    [Fact] // P4-U12, PART-4 section 7: invalid input → the form with the error; no CSV
    public async Task TradesCsv_InvalidBacktestInput_ShowsTheBacktestForm()
    {
        var run = await TradesCsvAsync("symbol=AAPL&strategy=fake-threshold&from=2021-01-01&to=2020-01-01");

        BacktestForm(run.Result).Error("from").Should().Be("The start date must be on or before the end date.");
        run.Backtests.Calls.Should().Be(0);
        run.Reports.CsvCalls.Should().BeEmpty();
    }

    [Theory] // P4-U12, PART-4 section 7: UNKNOWN, NOKEY, LIMIT → the form alert with its hints; no CSV
    [InlineData("UNKNOWN", "Symbol 'UNKNOWN' was not found.", null, null)]
    [InlineData("NOKEY", "No Alpha Vantage API key is configured.", FormAlert.MissingApiKeyHint, FormAlert.MissingApiKeyCommand)]
    [InlineData("LIMIT", "The daily Alpha Vantage limit (25 requests) is reached.", FormAlert.RateLimitHint, null)]
    public async Task TradesCsv_MarketDataErrors_ShowTheFormAlert(string symbol, string message, string? hint, string? command)
    {
        var run = await TradesCsvAsync(Backtest.Replace("symbol=AAPL", "symbol=" + symbol, StringComparison.Ordinal));

        BacktestForm(run.Result).Alert.Should().Be(new FormAlert(message, hint, command));
        run.Reports.CsvCalls.Should().BeEmpty();
    }

    [Fact] // CONTRACT section 12, P4-U12: an unexpected exception of the backtest goes to /Home/Error (not caught)
    public async Task TradesCsv_UnexpectedBacktestException_IsNotSwallowed()
    {
        var act = () => TradesCsvAsync(Backtest, backtests: new ThrowingBacktestService());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] // CONTRACT section 12: IReportGenerator declares no exception; anything it throws is a bug
    public async Task TradesCsv_UnexpectedGeneratorException_IsNotSwallowed()
    {
        var act = () => TradesCsvAsync(Backtest, reports: new RecordingReportGenerator(new InvalidOperationException("Bug.")));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class ThrowingBacktestService : IBacktestService
    {
        public Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bug.");
    }
}
