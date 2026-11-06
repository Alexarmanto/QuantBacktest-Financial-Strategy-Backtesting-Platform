using System.Reflection;
using System.Text.Json;
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
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

public class OptimizeControllerTests
{
    private const string ResultPath = "/Optimize/Result";

    private const string Settings = "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0";

    /// <summary>fake-threshold with the P4-X2 defaults: 20 × 21 = 420 cells.</summary>
    private const string Canonical = Settings + "&x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025&metric=sharpe";

    private static FakeStrategyCatalog FakeCatalog() => new(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());

    private static OptimizeController Controller(string queryString, IStrategyCatalog? catalog = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost:5080");
        httpContext.Request.Path = ResultPath;
        httpContext.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString("?" + queryString);
        return new OptimizeController(catalog ?? FakeCatalog(), new FakeMarketDataService(), NullLogger<OptimizeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static RecordingSweepService Sweep(IStrategyCatalog? catalog = null) =>
        new(new FakeParameterSweepService(catalog ?? FakeCatalog()), new FakeMarketDataService());

    private static async Task<IActionResult> ResultAsync(string queryString, RecordingSweepService? sweep = null, IStrategyCatalog? catalog = null) =>
        await Controller(queryString, catalog).Result(sweep ?? Sweep(catalog), CancellationToken.None);

    private static OptimizeResultViewModel ResultView(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().BeNull();
        return view.Model.Should().BeOfType<OptimizeResultViewModel>().Subject;
    }

    private static OptimizeFormViewModel FormView(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Index");
        return view.Model.Should().BeOfType<OptimizeFormViewModel>().Subject;
    }

    private static string RedirectQuery(IActionResult result)
    {
        var url = result.Should().BeOfType<RedirectResult>().Subject.Url;
        url.Should().StartWith(ResultPath + "?");
        return url[(ResultPath.Length + 1)..];
    }

    /// <summary>A result written by hand for the canonical grid: every cell 0.1, except the ones given.</summary>
    private static Func<SweepRequest, SweepResult> Scripted(Func<int, SweepCell, SweepCell>? change = null, bool isolated = false) => request =>
    {
        var xs = new List<double>();
        for (var k = 0; k < 20; k++)
        {
            xs.Add(1 + (5 * k));
        }

        var ys = Enumerable.Range(0, 21).Select(k => Math.Round(k * 0.025, 10)).ToList();
        var cells = ys.SelectMany(y => xs.Select(x => new SweepCell(x, y, 0.1, 10, null)))
            .Select((cell, i) => change is null ? cell : change(i, cell))
            .ToList();
        return new SweepResult(request, xs, ys, cells, cells[0], 0.1, isolated);
    };

    // ---- Routes ----

    [Fact] // PART-4 section 5 / P4-X2: GET /Optimize and GET /Optimize/Result only; the form does not depend on the service
    public void Optimize_Routes_AreGetOnly()
    {
        var actions = typeof(OptimizeController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        actions.Select(m => m.Name).Should().BeEquivalentTo(nameof(OptimizeController.Index), nameof(OptimizeController.Result));
        foreach (var action in actions)
        {
            action.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull(action.Name);
            action.GetCustomAttributes().OfType<IActionHttpMethodProvider>().SelectMany(a => a.HttpMethods).Should().Equal("GET");
        }

        typeof(OptimizeController).GetMethod(nameof(OptimizeController.Index))!.GetParameters()
            .Should().NotContain(p => p.ParameterType == typeof(IParameterSweepService));
        var sweep = typeof(OptimizeController).GetMethod(nameof(OptimizeController.Result))!.GetParameters()
            .Single(p => p.ParameterType == typeof(IParameterSweepService));
        sweep.GetCustomAttribute<FromServicesAttribute>().Should().NotBeNull();
    }

    // ---- Form ----

    [Fact] // P4-X2: the form lists the catalog and never runs a sweep
    public async Task Index_ShowsTheForm()
    {
        var form = FormView(await Controller("strategy=fake-threshold").Index(CancellationToken.None));

        form.Selected!.Id.Should().Be("fake-threshold");
        form.CachedSymbols.Should().NotBeEmpty();
    }

    // ---- Canonical URL ----

    [Fact] // P4-X2: no parameter at all → the form
    public async Task Result_WithoutParameters_RedirectsToTheForm()
    {
        var result = await ResultAsync("");

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
    }

    [Fact] // P4-X2: the canonical URL runs the sweep, without a redirect
    public async Task Result_CanonicalUrl_DoesNotRedirect()
    {
        var model = ResultView(await ResultAsync(Canonical));

        model.Permalink.Should().Be("http://localhost:5080" + ResultPath + "?" + Canonical);
        model.EditSettingsUrl.Should().Be("/Optimize?" + Canonical);
    }

    [Theory] // P4-X2: a valid non-canonical URL redirects exactly once, to the canonical URL, before any sweep
    [InlineData("symbol=aapl&strategy=fake-threshold")]
    [InlineData(Settings)]
    [InlineData(Settings + "&p.lookback=10&x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025&metric=sharpe")]
    [InlineData("metric=SHARPE&" + Settings + "&y=threshold&x=lookback")]
    [InlineData(Settings + "&x=lookback&xmin=1.0&xmax=100&xstep=5&y=threshold&ymin=0.000&ymax=.5&ystep=0.025&metric=sharpe&unknown=1")]
    public async Task Result_NonCanonicalUrl_RedirectsExactlyOnce(string queryString)
    {
        var sweep = Sweep();

        var target = RedirectQuery(await ResultAsync(queryString, sweep));

        target.Should().Be(Canonical);
        sweep.Requests.Should().BeEmpty();
        ResultView(await ResultAsync(target));
    }

    // ---- Form errors, nothing run ----

    [Theory] // O1 checks of the web (point 1 of the plan): the form with the error, no call to the service
    [InlineData("x=lookback&xmin=1&xmax=100&xstep=5&y=lookback&ymin=1&ymax=100&ystep=5", "y")]
    [InlineData("x=lookback&xmin=50&xmax=10&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025", "xmin")]
    [InlineData("x=lookback&xmin=1&xmax=100&xstep=0&y=threshold&ymin=0&ymax=0.5&ystep=0.025", "xstep")]
    [InlineData("x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=-0.025", "ystep")]
    [InlineData("x=lookback&xmin=1&xmax=100&xstep=2.5&y=threshold&ymin=0&ymax=0.5&ystep=0.025", "xstep")]
    [InlineData("x=foo&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025", "x")]
    [InlineData("x=lookback&xmin=1&xmax=1000&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025", "xmax")]
    [InlineData("x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025&metric=foo", "metric")]
    public async Task Result_InvalidAxes_ShowsFormWithError_NoCall(string axes, string field)
    {
        var sweep = Sweep();

        var form = FormView(await ResultAsync(Settings + "&" + axes, sweep));

        form.Error(field).Should().NotBeNull();
        sweep.Requests.Should().BeEmpty();
    }

    [Fact] // O1, Conventions.MaxSweepCells: too many cells → the form with the error at the top, no call
    public async Task Result_TooManyCells_ShowsFormError_NoCall()
    {
        var sweep = Sweep();

        var form = FormView(await ResultAsync(
            Settings + "&x=lookback&xmin=1&xmax=100&xstep=1&y=threshold&ymin=0&ymax=0.5&ystep=0.005&metric=sharpe", sweep));

        form.OtherErrors.Should().ContainSingle().Which.Should().StartWith("The heatmap would have 10100 cells (100 × 101); the maximum is 625.");
        form.Error("xstep").Should().Be(OptimizationQuery.TooManyCellsHint);
        form.Error("ystep").Should().Be(OptimizationQuery.TooManyCellsHint);
        form.Selected!.Y!.Step.Value.Should().Be("0.005");
        sweep.Requests.Should().BeEmpty();
    }

    [Fact] // PART-4 section 7 (invalid input), P4-U12: the field error and the input as typed, no call
    public async Task Result_InvalidSettings_ShowsFormWithInput()
    {
        var sweep = Sweep();

        var form = FormView(await ResultAsync(Canonical.Replace("capital=10000", "capital=-5"), sweep));

        form.Error("capital").Should().Be("Initial capital must be greater than 0.");
        form.Value("capital").Should().Be("-5");
        sweep.Requests.Should().BeEmpty();
    }

    [Theory] // O1, P4-U1: unknown strategy, unsupported timeframe, fewer than 2 parameters → strategy error, no call
    [InlineData("symbol=AAPL&strategy=nope", "Unknown strategy 'nope'.")]
    [InlineData("symbol=AAPL&tf=weekly&strategy=fake-alternating", "[FAKE] Alternating trades does not support the weekly timeframe (daily only).")]
    [InlineData("symbol=AAPL&strategy=custom-trend-dip", "fewer than 2 parameters")]
    public async Task Result_StrategyError_ShowsForm_NoCall(string queryString, string message)
    {
        var sweep = Sweep();

        var form = FormView(await ResultAsync(queryString, sweep));

        form.Error("strategy").Should().Contain(message);
        sweep.Requests.Should().BeEmpty();
    }

    // ---- The call ----

    [Fact] // O2, P4-X2: exactly one IParameterSweepService call, with the request of the URL
    public async Task Result_CallsSweepOnce_WithExpectedRequest()
    {
        var sweep = Sweep();

        ResultView(await ResultAsync(Canonical.Replace("fee=0.1", "fee=0.25"), sweep));

        var request = sweep.Requests.Should().ContainSingle().Subject;
        request.BaseRequest.Symbol.Should().Be("AAPL");
        request.BaseRequest.StrategyId.Should().Be("fake-threshold");
        request.BaseRequest.Timeframe.Should().Be(Timeframe.Daily);
        request.BaseRequest.Execution.FeeRate.Should().Be(0.0025m);
        request.BaseRequest.Parameters.Should().BeEmpty();
        request.X.Should().Be(new SweepAxis("lookback", 1, 100, 5));
        request.Y.Should().Be(new SweepAxis("threshold", 0, 0.5, 0.025));
        request.Metric.Should().Be(SweepMetric.SharpeRatio);
    }

    [Fact] // O5 with the Fakes: one cell per combination (20 × 21), the best cell outlined, the overfitting note
    public async Task Result_Valid_ShowsOneCellPerCombination()
    {
        var model = ResultView(await ResultAsync(Canonical));

        JsonDocument.Parse(model.Heatmap.Json).RootElement.GetProperty("cells").GetArrayLength().Should().Be(420);
        model.Heatmap.HasBest.Should().BeTrue();
        model.Heatmap.TableRows.Should().ContainSingle(r => r.Best);
        model.MetricLabel.Should().Be("Sharpe ratio");
    }

    [Fact] // O2: an invalid combination is a cell with its error, the others are shown, no exception
    public async Task Result_InvalidCombination_CellNullWithError_NotLinked()
    {
        var sweep = Sweep().Returning(Scripted((i, cell) => i == 1 ? cell with { Value = null, TradeCount = 0, Error = "Invalid pair." } : cell));

        var rows = ResultView(await ResultAsync(Canonical, sweep)).Heatmap.TableRows;

        rows[1].Note.Should().Be("Invalid pair.");
        rows[1].Url.Should().BeNull();
        rows[1].Value.Text.Should().Be("n/a");
        rows.Where((_, i) => i != 1).Should().OnlyContain(r => r.Url != null);
    }

    [Fact] // PART-4 section 7 ("cellule null rendue"): an undefined metric is a rendered cell, null in the JSON
    public async Task Result_NullCell_IsRendered()
    {
        var sweep = Sweep().Returning(Scripted((i, cell) => i == 2 ? cell with { Value = null } : cell));

        var model = ResultView(await ResultAsync(Canonical, sweep));

        var cell = JsonDocument.Parse(model.Heatmap.Json).RootElement.GetProperty("cells")[2];
        cell.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        cell.GetProperty("valueText").GetString().Should().Be("n/a");
    }

    [Theory] // PART-4 section 7 ("IsIsolatedPeak → bandeau"), O4
    [InlineData(true)]
    [InlineData(false)]
    public async Task Result_IsolatedPeak_ShowsBanner(bool isolated)
    {
        var sweep = Sweep().Returning(Scripted(isolated: isolated));

        ResultView(await ResultAsync(Canonical, sweep)).Heatmap.IsIsolatedPeak.Should().Be(isolated);
    }

    // ---- Errors of the services ----

    [Fact] // P4-U12: a BacktestValidationException of the service (empty period...) → the form with its message
    public async Task Result_ServiceValidationError_ShowsFormAlert()
    {
        var sweep = Sweep().Failing(new BacktestValidationException("The selected period must contain at least 2 bars."));

        var form = FormView(await ResultAsync(Canonical, sweep));

        form.Alert!.Message.Should().Be("The selected period must contain at least 2 bars.");
        form.Value("symbol").Should().Be("AAPL");
    }

    [Theory] // PART-4 section 7 (UNKNOWN, NOKEY, LIMIT), P4-U12: one alert on the form, with the hints, no error page
    [InlineData("UNKNOWN", null, null)]
    [InlineData("NOKEY", FormAlert.MissingApiKeyHint, FormAlert.MissingApiKeyCommand)]
    [InlineData("LIMIT", FormAlert.RateLimitHint, null)]
    public async Task Result_MarketDataError_ShowsFormAlert(string symbol, string? hint, string? command)
    {
        var sweep = Sweep();

        var form = FormView(await ResultAsync(Canonical.Replace("symbol=AAPL", "symbol=" + symbol), sweep));

        form.Alert.Should().NotBeNull();
        form.Alert!.Hint.Should().Be(hint);
        form.Alert.Command.Should().Be(command);
        sweep.Requests.Should().ContainSingle();
    }

    [Fact] // P4-U12: any other exception is a bug and goes to /Home/Error (not caught here)
    public async Task Optimize_UnexpectedException_IsNotSwallowed()
    {
        var sweep = Sweep().Failing(new InvalidOperationException("Bug."));

        var act = () => ResultAsync(Canonical, sweep);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Bug.");
    }

    [Fact] // P4-U12: an unexpected exception of the catalog is not caught either
    public async Task Optimize_UnexpectedCatalogException_IsNotSwallowed()
    {
        var act = () => ResultAsync(Canonical, Sweep(), new ThrowingCatalog());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] // P2-C1: FindAsync refusing a stored custom strategy is a refused request, shown on the form
    public async Task Result_InvalidStoredCustomStrategy_ShowsFormAlert()
    {
        var sweep = Sweep();

        var form = FormView(await ResultAsync(Canonical, sweep, new RefusingCatalog(FakeCatalog())));

        form.Alert!.Message.Should().Be("Stored definition is invalid.");
        sweep.Requests.Should().BeEmpty();
    }

    private sealed class ThrowingCatalog : IStrategyCatalog
    {
        public Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Catalog bug.");

        public Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Catalog bug.");
    }

    private sealed class RefusingCatalog(IStrategyCatalog inner) : IStrategyCatalog
    {
        public Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);

        public Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            throw new BacktestValidationException("Stored definition is invalid.");
    }
}
