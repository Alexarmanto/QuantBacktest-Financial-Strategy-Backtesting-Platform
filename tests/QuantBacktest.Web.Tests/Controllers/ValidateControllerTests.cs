using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

public class ValidateControllerTests
{
    private const string ResultPath = "/Validate/Result";

    private const string Settings = "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0";

    private const string Grid = "&x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025&metric=sharpe";

    /// <summary>fake-threshold, the P4-X2 default grid (20 × 21 cells), train=70.</summary>
    private const string Canonical = BacktestQueryValidationTests.Canonical;

    /// <summary>The services of one request, with one log for the order of the calls (GetSeries, Sweep, Backtest).</summary>
    private sealed class Fixture
    {
        public Fixture(PriceSeries? series = null, IStrategyCatalog? catalog = null)
        {
            Catalog = catalog ?? new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());
            var fakeCatalog = new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());
            Data = new RecordingMarketDataService(new FakeMarketDataService(), Log, series);
            Sweep = new LoggingSweepService(Log, new RecordingSweepService(new FakeParameterSweepService(fakeCatalog)));

            // The fake engine loads its own data, without the log: the log shows the web's calls only.
            Backtests = new LoggingBacktestService(
                Log, new FakeBacktestService(new RecordingMarketDataService(new FakeMarketDataService(), series: series), fakeCatalog));
        }

        public CallLog Log { get; } = new();

        public IStrategyCatalog Catalog { get; }

        public RecordingMarketDataService Data { get; }

        public LoggingSweepService Sweep { get; }

        public LoggingBacktestService Backtests { get; }

        public ValidateController Controller(string queryString)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost:5080");
            httpContext.Request.Path = ResultPath;
            httpContext.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString("?" + queryString);
            return new ValidateController(Catalog, Data, NullLogger<ValidateController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
            };
        }

        public Task<IActionResult> ResultAsync(string queryString) =>
            Controller(queryString).Result(Sweep, Backtests, CancellationToken.None);
    }

    /// <summary>100 daily bars from Monday 2018-01-01: at 70 %, train 2018-01-01..2018-04-06, test 2018-04-09..2018-05-18.</summary>
    private static PriceSeries HundredBars() => SyntheticPrices.Generate("AAPL", Timeframe.Daily, 100);

    private static ValidateResultViewModel ResultView(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().BeNull();
        return view.Model.Should().BeOfType<ValidateResultViewModel>().Subject;
    }

    private static ValidateFormViewModel FormView(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Index");
        return view.Model.Should().BeOfType<ValidateFormViewModel>().Subject;
    }

    private static string RedirectQuery(IActionResult result)
    {
        var url = result.Should().BeOfType<RedirectResult>().Subject.Url;
        url.Should().StartWith(ResultPath + "?");
        return url[(ResultPath.Length + 1)..];
    }

    // ---- Routes ----

    [Fact] // PART-4 section 5: GET /Validate and GET /Validate/Result only; the services of the run come from DI
    public void Validate_Routes_AreGetOnly()
    {
        var actions = typeof(ValidateController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        actions.Select(m => m.Name).Should().BeEquivalentTo(nameof(ValidateController.Index), nameof(ValidateController.Result));
        foreach (var action in actions)
        {
            action.GetCustomAttributes().OfType<IActionHttpMethodProvider>().SelectMany(a => a.HttpMethods).Should().Equal("GET");
        }

        var parameters = typeof(ValidateController).GetMethod(nameof(ValidateController.Result))!.GetParameters();
        foreach (var service in new[] { typeof(IParameterSweepService), typeof(IBacktestService) })
        {
            parameters.Single(p => p.ParameterType == service).GetCustomAttribute<FromServicesAttribute>().Should().NotBeNull();
        }

        typeof(ValidateController).GetMethod(nameof(ValidateController.Index))!.GetParameters()
            .Should().NotContain(p => p.ParameterType == typeof(IParameterSweepService) || p.ParameterType == typeof(IBacktestService));
    }

    // ---- Form ----

    [Fact] // P4-X3: the form, train 70 by default; nothing is loaded or run
    public async Task Index_ShowsTheForm_NoCall()
    {
        var fixture = new Fixture();

        var form = FormView(await fixture.Controller("strategy=fake-threshold").Index(CancellationToken.None));

        form.Sweep.Selected!.Id.Should().Be("fake-threshold");
        form.TrainValue.Should().Be("70");
        fixture.Log.Calls.Should().BeEmpty();
    }

    [Fact] // P4-X3: the link of the heatmap note prefills the form with the same grid
    public async Task Index_PrefilledFromTheHeatmapLink()
    {
        var fixture = new Fixture();
        var url = Settings + "&x=threshold&xmin=0.1&xmax=0.3&xstep=0.05&y=lookback&ymin=2&ymax=20&ystep=2&metric=calmar&train=70";

        var form = FormView(await fixture.Controller(url).Index(CancellationToken.None));

        form.Sweep.Selected!.X!.SelectedKey.Should().Be("threshold");
        form.Sweep.Selected.X.Min.Value.Should().Be("0.1");
        form.Sweep.Value(BacktestQuery.MetricKey).Should().Be("calmar");
        fixture.Log.Calls.Should().BeEmpty();
    }

    // ---- Canonical URL ----

    [Fact] // P4-X3: no parameter at all → the form
    public async Task Result_WithoutParameters_RedirectsToTheForm()
    {
        var result = await new Fixture().ResultAsync("");

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
    }

    [Fact] // P4-X3: the canonical URL runs, without a redirect; permalink and Edit settings carry it
    public async Task Result_CanonicalUrl_DoesNotRedirect()
    {
        var model = ResultView(await new Fixture().ResultAsync(Canonical));

        model.Permalink.Should().Be("http://localhost:5080" + ResultPath + "?" + Canonical);
        model.EditSettingsUrl.Should().Be("/Validate?" + Canonical);
    }

    [Theory] // P4-X3, decision 11: a valid non-canonical URL redirects exactly once to the canonical URL, before any call
    [InlineData("symbol=aapl&strategy=fake-threshold")]
    [InlineData(Settings + Grid)]
    [InlineData(Settings + Grid + "&train=70.0")]
    [InlineData("train=70&" + Settings + Grid)]
    [InlineData(Settings + Grid + "&train=70&unknown=1")]
    public async Task Result_NonCanonicalUrl_RedirectsExactlyOnce(string queryString)
    {
        var fixture = new Fixture();

        var target = RedirectQuery(await fixture.ResultAsync(queryString));

        target.Should().Be(Canonical);
        fixture.Log.Calls.Should().BeEmpty();
        ResultView(await new Fixture().ResultAsync(target));
    }

    [Fact] // decision 11: a decimal share is canonical in its shortest form (72.50 → 72.5)
    public async Task Result_DecimalTrain_ShortestForm()
    {
        RedirectQuery(await new Fixture().ResultAsync(Settings + Grid + "&train=72.50")).Should().Be(Settings + Grid + "&train=72.5");
    }

    // ---- Form errors: nothing is called ----

    [Theory] // T1, decision 7: train outside [50, 90], not a number or repeated → error under the field, no call at all
    [InlineData("49", "Training share must be between 50% and 90%.")]
    [InlineData("91", "Training share must be between 50% and 90%.")]
    [InlineData("abc", "Training share must be a number, with '.' as the decimal separator.")]
    [InlineData("70&train=80", "Training share is given more than once.")]
    public async Task Result_InvalidTrain_FieldError_NoCall(string train, string message)
    {
        var fixture = new Fixture();

        var form = FormView(await fixture.ResultAsync(Settings + Grid + "&train=" + train));

        form.TrainError.Should().Be(message);
        form.Sweep.Value("symbol").Should().Be("AAPL");
        fixture.Log.Calls.Should().BeEmpty();
    }

    [Theory] // P4-X2 rules, P4-U1: invalid settings, axes or strategy → the form, nothing loaded or run
    [InlineData(Settings + Grid + "&train=70&capital=-1", "capital")]
    [InlineData(Settings + "&x=lookback&y=lookback&train=70", "y")]
    [InlineData(Settings + "&xstep=1&ystep=0.005&train=70", "cells")]
    [InlineData("symbol=AAPL&strategy=nope", "strategy")]
    [InlineData("symbol=AAPL&tf=weekly&strategy=fake-alternating", "strategy")]
    public async Task Result_InvalidQuery_ShowsForm_NoCall(string queryString, string field)
    {
        var fixture = new Fixture();

        var form = FormView(await fixture.ResultAsync(queryString));

        (form.Sweep.Error(field) is not null || form.Sweep.OtherErrors.Count > 0).Should().BeTrue();
        fixture.Log.Calls.Should().BeEmpty();
    }

    // ---- T1: refused by the web, after the data, before the sweep ----

    [Fact] // T1, decision 7: a part shorter than 2 bars → alert at the top of the form, with N, nTrain and nTest; no sweep
    public async Task Result_PeriodTooShort_Alert_NoSweep()
    {
        var fixture = new Fixture();

        var form = FormView(await fixture.ResultAsync(
            "symbol=AAPL&tf=daily&strategy=fake-threshold&from=2018-01-01&to=2018-01-03&capital=10000&fee=0.1&slip=0&rf=0" + Grid + "&train=70"));

        form.Sweep.Alert!.Message.Should().Be(
            "The selected period has 3 bars: a training share of 70% gives 2 training bars and 1 test bar, and each part needs at least 2 bars. Choose a longer period or another training share.");
        form.TrainError.Should().BeNull();
        form.Sweep.Value("from").Should().Be("2018-01-01");
        fixture.Log.Calls.Should().Equal("GetSeries AAPL Daily refresh=False");
    }

    [Fact] // T1: an empty period → alert with the range of the series; no sweep
    public async Task Result_NoBarsInPeriod_Alert_NoSweep()
    {
        var fixture = new Fixture(HundredBars());

        var form = FormView(await fixture.ResultAsync(
            "symbol=AAPL&tf=daily&strategy=fake-threshold&from=2030-01-01&capital=10000&fee=0.1&slip=0&rf=0" + Grid + "&train=70"));

        form.Sweep.Alert!.Message.Should().Be("The selected period contains no bars of AAPL (daily): its data runs from 2018-01-01 to 2018-05-18.");
        fixture.Log.Calls.Should().Equal("GetSeries AAPL Daily refresh=False");
    }

    // ---- Calls to the contract ----

    [Fact] // P4-X3, T1 to T3: series (no refresh) → sweep on the train → backtest train → backtest test, with the exact requests
    public async Task Result_CallsInOrder_WithExactRequests()
    {
        var fixture = new Fixture(HundredBars());

        var model = ResultView(await fixture.ResultAsync(Canonical));

        fixture.Log.Calls.Should().Equal(
            "GetSeries AAPL Daily refresh=False",
            "Sweep 2018-01-01..2018-04-06",
            "Backtest 2018-01-01..2018-04-06",
            "Backtest 2018-04-09..2018-05-18");
        fixture.Data.SeriesRequests.Should().Equal(("AAPL", Timeframe.Daily, false));

        var sweep = fixture.Sweep.Requests.Should().ContainSingle().Subject;
        sweep.BaseRequest.Should().BeEquivalentTo(new
        {
            Symbol = "AAPL",
            Timeframe = Timeframe.Daily,
            StrategyId = "fake-threshold",
            From = (DateOnly?)new DateOnly(2018, 1, 1),
            To = (DateOnly?)new DateOnly(2018, 4, 6),
            Execution = new ExecutionSettings(10000m, 0.001m, 0m),
        });
        sweep.BaseRequest.Parameters.Should().BeEmpty();
        sweep.X.Should().Be(new SweepAxis("lookback", 1, 100, 5));
        sweep.Y.Should().Be(new SweepAxis("threshold", 0, 0.5, 0.025));
        sweep.Metric.Should().Be(SweepMetric.SharpeRatio);

        // The best cell of the fake sweep (O3 as it computes it): the web takes it as it is.
        var best = (await new FakeParameterSweepService(new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory()))
            .RunAsync(sweep)).Best!;
        var parameters = new Dictionary<string, double> { ["lookback"] = best.X, ["threshold"] = best.Y };

        fixture.Backtests.Requests.Should().HaveCount(2);
        var train = fixture.Backtests.Requests[0];
        var test = fixture.Backtests.Requests[1];
        train.Should().BeEquivalentTo(sweep.BaseRequest with { Parameters = parameters });
        test.Should().BeEquivalentTo(sweep.BaseRequest with
        {
            Parameters = parameters,
            From = new DateOnly(2018, 4, 9),
            To = new DateOnly(2018, 5, 18),
        });
        (train.Execution == sweep.BaseRequest.Execution && test.Execution == sweep.BaseRequest.Execution).Should().BeTrue();

        model.Split.Select(r => r.Value).Should().Equal(
            "100", "2018-01-01 to 2018-04-06 (70 bars)", "2018-04-09 to 2018-05-18 (30 bars)");
        model.Best!.Parameters.Select(p => p.Value).Should().Equal(BacktestQuery.FormatNumber(best.X), BacktestQuery.FormatNumber(best.Y));
    }

    [Fact] // P4-X3 "Aucune meilleure cellule → message clair": Best null → the page, no backtest at all
    public async Task Result_BestNull_ShowsMessage_NoBacktest()
    {
        var fixture = new Fixture(HundredBars());
        fixture.Sweep.Inner.Returning(request => new SweepResult(
            request, [1, 6], [0], [new SweepCell(1, 0, null, 0, "Invalid pair."), new SweepCell(6, 0, null, 0, "Invalid pair.")], null, null, false));

        var model = ResultView(await fixture.ResultAsync(Canonical));

        model.Best.Should().BeNull();
        model.Metrics.Should().BeEmpty();
        fixture.Backtests.Requests.Should().BeEmpty();
        fixture.Log.Calls.Should().Equal("GetSeries AAPL Daily refresh=False", "Sweep 2018-01-01..2018-04-06");
    }

    // ---- Errors of the services ----

    [Theory] // PART-4 section 7 (UNKNOWN, NOKEY, LIMIT), P4-U12: the first call fails → alert with the hints, nothing else runs
    [InlineData("UNKNOWN", null, null)]
    [InlineData("NOKEY", FormAlert.MissingApiKeyHint, FormAlert.MissingApiKeyCommand)]
    [InlineData("LIMIT", FormAlert.RateLimitHint, null)]
    public async Task Result_MarketDataError_FirstCall_Stops(string symbol, string? hint, string? command)
    {
        var fixture = new Fixture();

        var form = FormView(await fixture.ResultAsync(Canonical.Replace("symbol=AAPL", "symbol=" + symbol, StringComparison.Ordinal)));

        form.Sweep.Alert.Should().NotBeNull();
        form.Sweep.Alert!.Hint.Should().Be(hint);
        form.Sweep.Alert.Command.Should().Be(command);
        fixture.Log.Calls.Should().Equal($"GetSeries {symbol} Daily refresh=False");
    }

    [Theory] // P4-U12: a BacktestValidationException or MarketDataException at any later step → the alert of the form
    [InlineData("sweep", 2)]
    [InlineData("train", 3)]
    [InlineData("test", 4)]
    public async Task Result_ServiceError_AtAnyStep_ShowsAlert(string step, int calls)
    {
        foreach (Exception error in new Exception[]
                 {
                     new BacktestValidationException("Refused."),
                     new MarketDataException(MarketDataErrorKind.NetworkError, "Refused."),
                 })
        {
            var fixture = new Fixture(HundredBars());
            if (step == "sweep")
            {
                fixture.Sweep.Inner.Failing(error);
            }
            else
            {
                fixture.Backtests.Failing(error, fromCall: step == "train" ? 1 : 2);
            }

            var form = FormView(await fixture.ResultAsync(Canonical));

            form.Sweep.Alert!.Message.Should().Be("Refused.");
            fixture.Log.Calls.Should().HaveCount(calls);
        }
    }

    [Theory] // P4-U12: any other exception is a bug and goes to /Home/Error (not caught), wherever it happens
    [InlineData("data")]
    [InlineData("sweep")]
    [InlineData("backtest")]
    public async Task Validate_UnexpectedException_IsNotSwallowed(string step)
    {
        var fixture = new Fixture(HundredBars());
        var bug = new InvalidOperationException("Bug.");
        if (step == "data")
        {
            fixture.Data.Failing(bug);
        }
        else if (step == "sweep")
        {
            fixture.Sweep.Inner.Failing(bug);
        }
        else
        {
            fixture.Backtests.Failing(bug);
        }

        var act = () => fixture.ResultAsync(Canonical);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Bug.");
    }

    [Fact] // P2-C1: FindAsync refusing a stored custom strategy is a refused request, shown on the form; nothing loaded
    public async Task Result_InvalidStoredCustomStrategy_ShowsFormAlert()
    {
        var fixture = new Fixture(catalog: new RefusingCatalog(new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory())));

        var form = FormView(await fixture.ResultAsync(Canonical));

        form.Sweep.Alert!.Message.Should().Be("Stored definition is invalid.");
        fixture.Log.Calls.Should().BeEmpty();
    }

    private sealed class RefusingCatalog(IStrategyCatalog inner) : IStrategyCatalog
    {
        public Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);

        public Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            throw new BacktestValidationException("Stored definition is invalid.");
    }
}
