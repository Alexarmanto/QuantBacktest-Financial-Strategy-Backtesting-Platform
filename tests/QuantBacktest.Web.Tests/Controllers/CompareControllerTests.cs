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

public class CompareControllerTests
{
    private const string ResultPath = "/Compare/Result";

    /// <summary>The 3 strategies of the Fakes: fake-alternating (daily only), fake-threshold, custom-trend-dip.</summary>
    private const string Canonical =
        "symbol=AAPL&tf=daily&strategies=fake-alternating,fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0";

    private const string Weekly =
        "symbol=AAPL&tf=weekly&strategies=fake-alternating,fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0";

    private static FakeStrategyCatalog FakeCatalog() => new(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());

    private static CompareController Controller(string queryString, IStrategyCatalog? catalog = null, IMarketDataService? marketData = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost:5080");
        httpContext.Request.Path = ResultPath;
        httpContext.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString("?" + queryString);
        return new CompareController(
            catalog ?? FakeCatalog(),
            marketData ?? new FakeMarketDataService(),
            NullLogger<CompareController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static (ScriptedBacktestService Service, IStrategyCatalog Catalog) Engine(IStrategyCatalog? catalog = null)
    {
        catalog ??= FakeCatalog();
        return (new ScriptedBacktestService(new FakeBacktestService(new FakeMarketDataService(), catalog)), catalog);
    }

    private static async Task<IActionResult> ResultAsync(string queryString, ScriptedBacktestService? service = null, IStrategyCatalog? catalog = null)
    {
        if (service is null)
        {
            (service, catalog) = Engine(catalog);
        }

        return await Controller(queryString, catalog).Result(service, CancellationToken.None);
    }

    private static CompareResultViewModel ResultView(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().BeNull();
        return view.Model.Should().BeOfType<CompareResultViewModel>().Subject;
    }

    private static CompareFormViewModel FormView(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Index");
        return view.Model.Should().BeOfType<CompareFormViewModel>().Subject;
    }

    private static async Task<CompareFormViewModel> IndexAsync(string queryString) =>
        FormView(await Controller(queryString).Index(CancellationToken.None));

    private static string RedirectQuery(IActionResult result)
    {
        var url = result.Should().BeOfType<RedirectResult>().Subject.Url;
        url.Should().StartWith(ResultPath + "?");
        return url[(ResultPath.Length + 1)..];
    }

    // ---- Routes ----

    [Fact] // P4-X1, PART-4 section 6: GET /Compare and GET /Compare/Result only; the form does not depend on the engine
    public void Compare_Routes_AreGetOnly()
    {
        foreach (var name in new[] { nameof(CompareController.Index), nameof(CompareController.Result) })
        {
            var method = typeof(CompareController).GetMethod(name)!;
            method.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
            method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().BeNull("the conventional route gives /Compare/" + name);
            method.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
        }

        typeof(CompareController).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType == typeof(IBacktestService));
        typeof(CompareController).GetMethod(nameof(CompareController.Result))!.GetParameters()
            .Should().Contain(p => p.ParameterType == typeof(IBacktestService)
                && p.GetCustomAttributes(typeof(FromServicesAttribute), false).Length == 1);
    }

    // ---- GET /Compare ----

    [Fact] // P4-X1, P4-U1: one box per catalog strategy, built-in then custom, none checked on a blank form
    public async Task Index_ListsCatalogStrategies_NoneChecked()
    {
        var form = await IndexAsync("");

        form.BuiltInStrategies.Select(o => o.Id).Should().Equal("fake-alternating", "fake-threshold");
        form.CustomStrategies.Select(o => o.Id).Should().Equal("custom-trend-dip");
        form.AllStrategies.Should().OnlyContain(o => !o.IsChecked && !o.IsDisabled);
        form.CountText.Should().Be("0 selected (2 to 5)");
        form.Value(BacktestQuery.SymbolKey).Should().Be("AAPL");
        form.Value(BacktestQuery.FeeKey).Should().Be("0.1");
        form.HasErrors.Should().BeFalse();
    }

    [Fact] // P4-X1, P4-U12: prefilled from the URL (settings and checked boxes)
    public async Task Index_PrefillsFromUrl()
    {
        var form = await IndexAsync("symbol=MSFT&tf=weekly&strategies=fake-threshold,custom-trend-dip&capital=5000");

        form.Value(BacktestQuery.SymbolKey).Should().Be("MSFT");
        form.Value(BacktestQuery.TimeframeKey).Should().Be("weekly");
        form.Value(BacktestQuery.CapitalKey).Should().Be("5000");
        form.AllStrategies.Where(o => o.IsChecked).Select(o => o.Id).Should().Equal("fake-threshold", "custom-trend-dip");
        form.CountText.Should().Be("2 selected (2 to 5)");
    }

    [Fact] // PART-4 section 7 (form), P4-U1: a daily-only strategy is disabled in weekly, with the reason, and unchecked
    public async Task Index_Weekly_DisablesDailyOnlyStrategy()
    {
        var form = await IndexAsync("tf=weekly&strategies=fake-alternating,fake-threshold");

        var alternating = form.AllStrategies.Single(o => o.Id == "fake-alternating");
        alternating.IsDisabled.Should().BeTrue();
        alternating.IsChecked.Should().BeFalse();
        alternating.Label.Should().Be("[FAKE] Alternating trades (daily only)");
        form.AllStrategies.Single(o => o.Id == "fake-threshold").IsDisabled.Should().BeFalse();
    }

    [Fact] // correction 1 of PR 2: the datalist is a convenience, the comparator form works without it
    public async Task Index_WhenCachedSymbolsFail_ShowsTheFormWithAnEmptyDatalist()
    {
        var view = await Controller("", marketData: new FailingCachedSymbolsMarketData()).Index(CancellationToken.None);

        var form = FormView(view);
        form.CachedSymbols.Should().BeEmpty();
        form.AllStrategies.Should().NotBeEmpty();
    }

    // ---- GET /Compare/Result: permalinks ----

    [Fact] // P4-X1: no parameter at all → the form
    public async Task Result_WithoutParameters_RedirectsToTheForm()
    {
        var result = await ResultAsync("");

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(CompareController.Index));
    }

    [Theory] // P4-X1: a canonical URL runs the comparison, even with an encoded comma
    [InlineData(Canonical)]
    [InlineData("symbol=AAPL&tf=daily&strategies=fake-threshold%2Ccustom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0")]
    public async Task Result_CanonicalUrl_DoesNotRedirect(string queryString)
    {
        ResultView(await ResultAsync(queryString)).Rows.Should().NotBeEmpty();
    }

    [Theory] // P4-X1: a valid non-canonical URL redirects exactly once, to the canonical URL
    [InlineData("symbol=aapl&strategies=fake-threshold,custom-trend-dip")]
    [InlineData("symbol=AAPL&strategies=fake-threshold&strategies=custom-trend-dip")]
    [InlineData("symbol=AAPL&tf=daily&strategies=fake-threshold,custom-trend-dip&from=&to=&capital=10000&fee=0.1&slip=0&sl=&tp=&ts=&rf=0")]
    [InlineData("symbol=AAPL&strategies=fake-threshold,custom-trend-dip&p.lookback=5&strategy=fake-threshold")]
    [InlineData("symbol=AAPL&strategies=fake-threshold,custom-trend-dip&sims=10&seed=1&mc=true&foo=bar")]
    [InlineData("symbol=AAPL&strategies=fake-threshold,,custom-trend-dip,")]
    [InlineData("Strategies=fake-threshold,custom-trend-dip&Symbol=AAPL")]
    public async Task Result_NonCanonicalUrl_RedirectsExactlyOnce(string queryString)
    {
        var target = RedirectQuery(await ResultAsync(queryString));

        target.Should().Be("symbol=AAPL&tf=daily&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0");
        ResultView(await ResultAsync(target)).Rows.Should().HaveCount(3);
    }

    [Fact] // P4-X1: the redirect happens before any backtest
    public async Task Result_Redirect_RunsNoBacktest()
    {
        var (service, catalog) = Engine();

        await ResultAsync("symbol=aapl&strategies=fake-threshold,custom-trend-dip", service, catalog);

        service.Requests.Should().BeEmpty();
    }

    // ---- Form errors, nothing run ----

    [Theory] // C1, P4-X1: fewer than 2, more than Max, a duplicate → the form with the error, no backtest
    [InlineData("symbol=AAPL&strategies=fake-threshold", "Select at least 2 strategies to compare.")]
    [InlineData("symbol=AAPL&strategies=a,b,c,d,e,f", "Select at most 5 strategies to compare.")]
    [InlineData("symbol=AAPL&strategies=fake-threshold,fake-threshold", "Strategy 'fake-threshold' is selected more than once.")]
    public async Task Result_InvalidStrategies_ShowsFormWithError(string queryString, string message)
    {
        var (service, catalog) = Engine();

        var form = FormView(await ResultAsync(queryString, service, catalog));

        form.StrategiesError.Should().Be(message);
        form.HasErrors.Should().BeTrue();
        form.Value(BacktestQuery.SymbolKey).Should().Be("AAPL");
        service.Requests.Should().BeEmpty();
    }

    [Theory] // PART-4 section 7 (invalid input), P4-U12: the form with the field error and the input as typed, no backtest
    [InlineData("symbol=bad*&strategies=fake-threshold,custom-trend-dip", BacktestQuery.SymbolKey, "bad*")]
    [InlineData("symbol=AAPL&strategies=fake-threshold,custom-trend-dip&capital=-5", BacktestQuery.CapitalKey, "-5")]
    [InlineData("symbol=AAPL&strategies=fake-threshold,custom-trend-dip&fee=9", BacktestQuery.FeeKey, "9")]
    [InlineData("symbol=AAPL&strategies=fake-threshold,custom-trend-dip&from=2024-02-01&to=2024-01-01", BacktestQuery.FromKey, "2024-02-01")]
    public async Task Result_InvalidSettings_ShowsFormWithInput(string queryString, string field, string typed)
    {
        var (service, catalog) = Engine();

        var form = FormView(await ResultAsync(queryString, service, catalog));

        form.Error(field).Should().NotBeNull();
        form.Value(field).Should().Be(typed);
        form.AllStrategies.Where(o => o.IsChecked).Select(o => o.Id).Should().Equal("fake-threshold", "custom-trend-dip");
        service.Requests.Should().BeEmpty();
    }

    // ---- Errors of one strategy (C-5) ----

    [Fact] // PART-4 section 7 ("comparateur avec une stratégie en erreur"), C-5: checked by the web, the service is not called
    public async Task Result_OneStrategyInError_OthersAreShown()
    {
        var (service, catalog) = Engine();

        var model = ResultView(await ResultAsync(Weekly, service, catalog));

        model.Errors.Should().Equal(new CompareError(
            "[FAKE] Alternating trades",
            "[FAKE] Alternating trades does not support the weekly timeframe (daily only)."));
        model.Rows.Select(r => r.Name).Should().Equal("[FAKE] Threshold on past return", "Trend dip", "Buy & hold");
        service.Requests.Select(r => r.StrategyId).Should().Equal("fake-threshold", "custom-trend-dip");
    }

    [Fact] // C-5: the timeframe message is the one of /Backtest (StrategyChecks), not a new text
    public async Task Result_UnsupportedTimeframe_UsesTheMessageOfTheBacktestForm()
    {
        var alternating = (await FakeCatalog().FindAsync("fake-alternating"))!;

        var model = ResultView(await ResultAsync(Weekly));

        model.Errors.Single().Message.Should().Be(StrategyChecks.TimeframeError(alternating, Timeframe.Weekly));
    }

    [Fact] // C-5: a BacktestValidationException of the service is that strategy's error, its message shown as received
    public async Task Result_ServiceValidationError_IsThatStrategyError()
    {
        var (service, catalog) = Engine();
        service.Failing("fake-threshold", new BacktestValidationException("Parameter 'lookback' is invalid for this series."));

        var model = ResultView(await ResultAsync(Canonical, service, catalog));

        model.Errors.Should().Equal(new CompareError("[FAKE] Threshold on past return", "Parameter 'lookback' is invalid for this series."));
        model.Rows.Select(r => r.Name).Should().Equal("[FAKE] Alternating trades", "Trend dip", "Buy & hold");
        service.Requests.Should().HaveCount(3);
    }

    [Fact] // C-5, decision D4: an unknown id (a deleted custom strategy) is that strategy's error, the others still run
    public async Task Result_UnknownStrategy_IsThatStrategyError()
    {
        var (service, catalog) = Engine();
        const string query =
            "symbol=AAPL&tf=daily&strategies=fake-threshold,custom-deleted,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0";

        var model = ResultView(await ResultAsync(query, service, catalog));

        model.Errors.Should().Equal(new CompareError("custom-deleted", "Unknown strategy 'custom-deleted'."));
        model.Errors.Single().Message.Should().Be(StrategyChecks.UnknownStrategy("custom-deleted"));
        model.Rows.Should().HaveCount(3);
        service.Requests.Select(r => r.StrategyId).Should().Equal("fake-threshold", "custom-trend-dip");
    }

    [Fact] // C-5: FindAsync refusing a stored custom strategy (BacktestValidationException) is that strategy's error
    public async Task Result_InvalidStoredCustomStrategy_IsThatStrategyError()
    {
        var catalog = new InvalidCustomStrategyCatalog(FakeCatalog(), "custom-trend-dip");
        var (service, _) = Engine(catalog);

        var model = ResultView(await ResultAsync(Canonical, service, catalog));

        model.Errors.Should().Equal(new CompareError("custom-trend-dip", InvalidCustomStrategyCatalog.Problems));
        service.Requests.Select(r => r.StrategyId).Should().Equal("fake-alternating", "fake-threshold");
    }

    [Fact] // decision D2: the same BacktestValidationException for every strategy is listed per strategy, never a form
    public async Task Result_SameValidationErrorForAll_IsListedPerStrategy()
    {
        const string query =
            "symbol=AAPL&tf=daily&strategies=fake-threshold,custom-trend-dip&from=2040-01-01&capital=10000&fee=0.1&slip=0&rf=0";

        var model = ResultView(await ResultAsync(query));

        model.Errors.Should().Equal(
            new CompareError("[FAKE] Threshold on past return", "Not enough bars in the selected period."),
            new CompareError("Trend dip", "Not enough bars in the selected period."));
        model.NothingRun.Should().BeTrue();
        model.Charts.Should().BeNull();
        model.Rows.Should().BeEmpty();
        model.EditSettingsUrl.Should().Be("/Compare?" + query);
    }

    [Fact] // decision D2: every strategy failing → the comparison page with the errors, no chart, no table
    public async Task Result_AllFail_ShowsTheErrorsWithoutChartOrTable()
    {
        var (service, catalog) = Engine();
        service.Failing("fake-threshold", new BacktestValidationException("A."))
            .Failing("custom-trend-dip", new BacktestValidationException("B."));
        const string query = "symbol=AAPL&tf=daily&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0";

        var model = ResultView(await ResultAsync(query, service, catalog));

        model.Errors.Select(e => e.Message).Should().Equal("A.", "B.");
        model.NothingRun.Should().BeTrue();
        model.Charts.Should().BeNull();
        model.Period.Should().Be("n/a");
    }

    // ---- Common error: market data ----

    [Theory] // PART-4 section 7 (UNKNOWN, NOKEY, LIMIT), P4-U12: one alert on the form, after the first call only
    [InlineData("UNKNOWN", null)]
    [InlineData("NOKEY", FormAlert.MissingApiKeyHint)]
    [InlineData("LIMIT", FormAlert.RateLimitHint)]
    public async Task Result_MarketDataError_ShowsFormAlert_StopsAfterFirstCall(string symbol, string? hint)
    {
        var (service, catalog) = Engine();
        var query = $"symbol={symbol}&tf=daily&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0";

        var form = FormView(await ResultAsync(query, service, catalog));

        form.Alert.Should().NotBeNull();
        form.Alert!.Hint.Should().Be(hint);
        form.Value(BacktestQuery.SymbolKey).Should().Be(symbol);
        form.AllStrategies.Where(o => o.IsChecked).Select(o => o.Id).Should().Equal("fake-threshold", "custom-trend-dip");
        service.Requests.Should().ContainSingle();
    }

    // ---- Calls (C1) ----

    [Fact] // C1: one IBacktestService call per strategy, same request except the strategy and its default parameters
    public async Task Result_CallsRunAsyncOncePerStrategy_SameRequestExceptIdAndDefaults()
    {
        var (service, catalog) = Engine();
        const string query =
            "symbol=AAPL&tf=daily&strategies=fake-alternating,fake-threshold,custom-trend-dip&from=2022-01-03&to=2024-06-28"
            + "&capital=25000&fee=0.2&slip=0.05&sl=5&tp=20&ts=3&rf=2";

        ResultView(await ResultAsync(query, service, catalog));

        service.Requests.Select(r => r.StrategyId).Should().Equal("fake-alternating", "fake-threshold", "custom-trend-dip");
        service.Requests[0].Parameters.Should().BeEquivalentTo(new Dictionary<string, double> { ["period"] = 20, ["offset"] = 0 });
        service.Requests[1].Parameters.Should().BeEquivalentTo(new Dictionary<string, double> { ["lookback"] = 10, ["threshold"] = 0.02 });
        service.Requests[2].Parameters.Should().BeEmpty();
        foreach (var request in service.Requests.Skip(1))
        {
            request.Should().BeEquivalentTo(service.Requests[0], o => o.Excluding(r => r.StrategyId).Excluding(r => r.Parameters));
        }

        service.Requests[0].Execution.Should().Be(new ExecutionSettings(25000m, 0.002m, 0.0005m, 0.05m, 0.2m, 0.03m, 0.02));
    }

    // ---- Page ----

    [Fact] // P4-X1: permalink, Edit settings, links to the detailed results, single benchmark row
    public async Task Result_Valid_ShowsRowsLinksAndPermalink()
    {
        var model = ResultView(await ResultAsync(Canonical));

        model.Permalink.Should().Be("http://localhost:5080" + ResultPath + "?" + Canonical);
        model.EditSettingsUrl.Should().Be("/Compare?" + Canonical);
        model.Rows[1].DetailUrl.Should().Be(
            "/Backtest/Result?symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02");
        model.Rows[^1].Name.Should().Be(BacktestResultViewModel.BenchmarkColumn);
        model.Rows[^1].DetailUrl.Should().BeNull();
        model.Charts.Should().NotBeNull();
        model.Errors.Should().BeEmpty();
    }

    [Fact] // C-6: the detailed link is the canonical URL of /Backtest/Result: following it never redirects
    public async Task Result_DetailLinks_AreCanonicalResultUrls()
    {
        var model = ResultView(await ResultAsync(Canonical));
        var catalog = FakeCatalog();

        foreach (var row in model.Rows.Where(r => r.DetailUrl is not null))
        {
            var queryString = row.DetailUrl![(ResultActionsViewModel.ResultPath.Length + 1)..];
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = ResultActionsViewModel.ResultPath;
            httpContext.Request.QueryString = new QueryString("?" + queryString);
            var backtest = new BacktestController(catalog, new FakeMarketDataService(), NullLogger<BacktestController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
            };

            var result = await backtest.Result(new FakeBacktestService(new FakeMarketDataService(), catalog), CancellationToken.None);

            result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<BacktestResultViewModel>();
        }
    }

    [Fact] // P4-U12: any other exception is a bug and goes to /Home/Error (not caught here), even on a later strategy
    public async Task Result_UnexpectedException_IsNotSwallowed()
    {
        var (service, catalog) = Engine();
        service.Failing("fake-threshold", new InvalidOperationException("Bug."));

        var act = () => ResultAsync(Canonical, service, catalog);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Bug.");
        service.Requests.Select(r => r.StrategyId).Should().Equal("fake-alternating", "fake-threshold");
    }

    [Fact] // P4-U12: an unexpected exception of the catalog is not caught either
    public async Task Result_UnexpectedCatalogException_IsNotSwallowed()
    {
        var catalog = new ThrowingCatalog(FakeCatalog());
        var (service, _) = Engine(catalog);

        var act = () => ResultAsync(Canonical, service, catalog);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class InvalidCustomStrategyCatalog(IStrategyCatalog inner, string invalidId) : IStrategyCatalog
    {
        public const string Problems = "Entry rule: condition 1: SMA period must be between 1 and 500.";

        public Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);

        public Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            id == invalidId ? throw new BacktestValidationException(Problems) : inner.FindAsync(id, cancellationToken);
    }

    private sealed class ThrowingCatalog(IStrategyCatalog inner) : IStrategyCatalog
    {
        public Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default) => inner.GetAllAsync(cancellationToken);

        public Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Catalog bug.");
    }

    private sealed class FailingCachedSymbolsMarketData : IMarketDataService
    {
        private readonly FakeMarketDataService _inner = new();

        public Task<PriceSeries> GetSeriesAsync(string symbol, Timeframe timeframe, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            _inner.GetSeriesAsync(symbol, timeframe, forceRefresh, cancellationToken);

        public Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("Cache unavailable.");

        public Task<DataQualityReport> GetQualityReportAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
            _inner.GetQualityReportAsync(symbol, timeframe, cancellationToken);

        public Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default) =>
            _inner.GetApiQuotaAsync(cancellationToken);
    }
}
