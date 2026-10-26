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

public class BacktestControllerTests
{
    private const string ResultPath = "/Backtest/Result";

    /// <summary>fake-threshold supports daily and weekly; lookback int 10 [1, 100], threshold dec 0.02 [0, 0.5].</summary>
    private const string Canonical =
        "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02";

    private static FakeStrategyCatalog FakeCatalog() => new(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());

    private static BacktestController Controller(
        string queryString,
        IStrategyCatalog? catalog = null,
        IMarketDataService? marketData = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost:5080");
        httpContext.Request.Path = ResultPath;
        httpContext.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString("?" + queryString);
        return new BacktestController(
            catalog ?? FakeCatalog(),
            marketData ?? new FakeMarketDataService(),
            NullLogger<BacktestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static Task<IActionResult> ResultAsync(string queryString, IStrategyCatalog? catalog = null)
    {
        catalog ??= FakeCatalog();
        var marketData = new FakeMarketDataService();
        return Controller(queryString, catalog, marketData)
            .Result(new FakeBacktestService(marketData, catalog), CancellationToken.None);
    }

    private static async Task<BacktestFormViewModel> FormAsync(string queryString)
    {
        var view = (await ResultAsync(queryString)).Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Index");
        return view.Model.Should().BeOfType<BacktestFormViewModel>().Subject;
    }

    private static async Task<BacktestFormViewModel> IndexAsync(string queryString, IStrategyCatalog? catalog = null, IMarketDataService? marketData = null)
    {
        var view = (await Controller(queryString, catalog, marketData).Index(CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
        return view.Model.Should().BeOfType<BacktestFormViewModel>().Subject;
    }

    private static string RedirectQuery(IActionResult result)
    {
        var url = result.Should().BeOfType<RedirectResult>().Subject.Url;
        url.Should().StartWith(ResultPath + "?");
        return url[(ResultPath.Length + 1)..];
    }

    // ---- GET /Backtest (P4-U1) ----

    [Fact] // P4-U1, decision E: blank form = defaults, preferred built-in strategy, first cached symbol
    public async Task Index_BlankForm_ShowsTheDefaults()
    {
        var catalog = new StubStrategyCatalog(
            new StubStrategy { Id = "buy-and-hold" },
            new StubStrategy { Id = BacktestFormViewModel.PreferredStrategyId });

        var form = await IndexAsync("", catalog);

        form.Value(BacktestQuery.SymbolKey).Should().Be("AAPL");
        form.Value(BacktestQuery.TimeframeKey).Should().Be("daily");
        form.Value(BacktestQuery.StrategyKey).Should().Be("sma-crossover");
        form.Value(BacktestQuery.CapitalKey).Should().Be("10000");
        form.Value(BacktestQuery.FeeKey).Should().Be("0.1");
        form.Value(BacktestQuery.SlippageKey).Should().Be("0");
        form.Value(BacktestQuery.RiskFreeKey).Should().Be("0");
        form.Value(BacktestQuery.StopLossKey).Should().BeEmpty();
        form.Value(BacktestQuery.FromKey).Should().BeEmpty();
        form.HasErrors.Should().BeFalse();
        form.Alert.Should().BeNull();
    }

    [Fact] // decision E: without the preferred strategy, the first one of the catalog
    public async Task Index_WithoutPreferredStrategy_SelectsTheFirst()
    {
        var form = await IndexAsync("", new StubStrategyCatalog(new StubStrategy { Id = "first" }, new StubStrategy { Id = "second" }));

        form.Value(BacktestQuery.StrategyKey).Should().Be("first");
        form.AllStrategies.Single(s => s.IsSelected).Id.Should().Be("first");
    }

    [Fact] // P4-U1, PART-4 section 7: strategies of the catalog and their parameters, built from Parameters
    public async Task Index_ListsTheCatalogStrategies_WithTheirParameters()
    {
        var catalog = FakeCatalog();
        var expected = await catalog.GetAllAsync();

        var form = await IndexAsync("", catalog);

        form.AllStrategies.Select(s => s.Id).Should().Equal(expected.Select(s => s.Id));
        form.CustomStrategies.Should().NotBeEmpty().And.OnlyContain(s => s.Id.StartsWith("custom-"));
        var threshold = form.AllStrategies.Single(s => s.Id == "fake-threshold").Parameters;
        threshold.Should().BeEquivalentTo(new[]
        {
            new ParameterField("p.lookback", "Lookback (bars)", "10", "1", "100", "1", null, null),
            new ParameterField("p.threshold", "Threshold (fraction)", "0.02", "0", "0.5", "0.005", "0.02 means 2 %.", null),
        }, o => o.WithStrictOrdering());
    }

    [Fact] // P4-U1: only one strategy's fields are enabled, so the others are never submitted
    public async Task Index_SelectsExactlyOneStrategy()
    {
        var form = await IndexAsync("strategy=fake-threshold");

        form.AllStrategies.Where(s => s.IsSelected).Select(s => s.Id).Should().Equal("fake-threshold");
    }

    [Fact] // P4-U1, PART-4 section 7: prefilled from the URL ("Edit settings"), raw, without validation
    public async Task Index_IsPrefilledFromTheUrl()
    {
        var form = await IndexAsync("symbol=msft&tf=weekly&strategy=fake-threshold&capital=-5&sl=7.5&p.lookback=30");

        form.Value(BacktestQuery.SymbolKey).Should().Be("msft");
        form.Value(BacktestQuery.TimeframeKey).Should().Be("weekly");
        form.Value(BacktestQuery.CapitalKey).Should().Be("-5");
        form.Value(BacktestQuery.StopLossKey).Should().Be("7.5");
        form.Value(BacktestQuery.FeeKey).Should().Be("0.1");
        form.AllStrategies.Single(s => s.IsSelected).Parameters.Select(p => p.Value).Should().Equal("30", "0.02");
        form.HasErrors.Should().BeFalse();
    }

    [Fact] // P4-U1, PART-4 section 7: a daily-only strategy carries its restriction (fake-alternating)
    public async Task Index_DailyOnlyStrategy_CarriesItsRestriction()
    {
        var form = await IndexAsync("tf=weekly");

        var alternating = form.AllStrategies.Single(s => s.Id == "fake-alternating");
        alternating.Timeframes.Should().Be("daily");
        alternating.Restriction.Should().Be("daily only");
        form.AllStrategies.Single(s => s.Id == "fake-threshold").Restriction.Should().BeNull();
    }

    [Fact] // correction 1: the cached symbols are a convenience; a failure leaves the datalist empty
    public async Task Index_WhenCachedSymbolsFail_ShowsTheFormWithAnEmptyDatalist()
    {
        var form = await IndexAsync("", marketData: new FailingCachedSymbolsMarketData());

        form.CachedSymbols.Should().BeEmpty();
        form.Value(BacktestQuery.SymbolKey).Should().BeEmpty();
        form.AllStrategies.Should().NotBeEmpty();
    }

    [Fact] // correction 1: the form does not depend on the engine; only Result receives IBacktestService
    public void Index_DoesNotDependOnTheEngine()
    {
        typeof(BacktestController).GetConstructors().Single().GetParameters()
            .Should().NotContain(p => p.ParameterType == typeof(IBacktestService));
        typeof(BacktestController).GetMethod(nameof(BacktestController.Result))!.GetParameters()
            .Should().Contain(p => p.ParameterType == typeof(IBacktestService)
                && p.GetCustomAttributes(typeof(FromServicesAttribute), false).Length == 1);
    }

    // ---- GET /Backtest/Result: permalinks (PART-4 section 5) ----

    [Fact] // decision A: no parameter at all → the form
    public async Task Result_WithoutParameters_RedirectsToTheForm()
    {
        var result = await ResultAsync("");

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be(nameof(BacktestController.Index));
    }

    [Theory] // PART-4 section 5: a canonical URL runs the backtest, even with needlessly encoded characters
    [InlineData(Canonical)]
    [InlineData("symbol=%41APL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02")]
    [InlineData("symbol=BRK%2EB&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02")]
    [InlineData("symbol=BRK.B&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02")]
    public async Task Result_CanonicalUrl_DoesNotRedirect(string queryString)
    {
        var result = await ResultAsync(queryString);

        result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<BacktestResultViewModel>();
    }

    [Theory] // PART-4 section 5, corrections 4 and 5: a valid non-canonical URL redirects exactly once
    [InlineData("symbol=aapl&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02")]
    [InlineData("symbol=AAPL&tf=daily&strategy=fake-threshold&from=&to=&capital=10000&fee=0.1&slip=0&sl=&tp=&ts=&rf=0&p.lookback=10&p.threshold=0.02")]
    [InlineData("strategy=fake-threshold&symbol=AAPL&p.threshold=0.020&p.lookback=10.0&capital=10000.00")]
    [InlineData("symbol=AAPL&strategy=fake-threshold&mc=true&sims=1000&foo=bar")]
    [InlineData("symbol=AAPL&strategy=fake-threshold")]
    [InlineData("symbol=AAPL&strategy=fake-threshold&P.lookback=10")]
    [InlineData("Symbol=AAPL&strategy=fake-threshold")]
    public async Task Result_NonCanonicalUrl_RedirectsExactlyOnce(string queryString)
    {
        var first = await ResultAsync(queryString);

        var target = RedirectQuery(first);
        target.Should().Be(Canonical);
        var second = await ResultAsync(target);
        second.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<BacktestResultViewModel>();
    }

    [Fact] // correction 5: the canonical URL carries every effective p.* (received values and catalog defaults)
    public async Task Result_Redirect_PinsTheStrategyDefaults()
    {
        var target = RedirectQuery(await ResultAsync("symbol=AAPL&strategy=fake-threshold&p.threshold=0.1"));

        target.Should().EndWith("&p.lookback=10&p.threshold=0.1");
    }

    // ---- Invalid input (P4-U12): form again, errors under their field, raw input kept, never a redirect ----

    [Theory] // P4-U12, PART-4 section 7
    [InlineData("symbol=AA%24PL&strategy=fake-threshold", "symbol", "AA$PL")]
    [InlineData("symbol=%5EGSPC&strategy=fake-threshold", "symbol", "^GSPC")]
    [InlineData("symbol=AAPL&strategy=fake-threshold&from=2024-02-01&to=2024-01-01", "from", "2024-02-01")]
    [InlineData("symbol=AAPL&strategy=fake-threshold&capital=-5", "capital", "-5")]
    [InlineData("symbol=AAPL&strategy=fake-threshold&fee=10", "fee", "10")]
    [InlineData("symbol=AAPL&strategy=fake-threshold&p.lookback=500", "p.lookback", "500")]
    [InlineData("symbol=AAPL&strategy=fake-threshold&p.lookback=2.5", "p.lookback", "2.5")]
    [InlineData("symbol=AAPL&strategy=no-such-strategy", "strategy", null)]
    [InlineData("symbol=AAPL&tf=weekly&strategy=fake-alternating", "strategy", "fake-alternating")]
    [InlineData("symbol=AAPL&symbol=MSFT&strategy=fake-threshold", "symbol", "AAPL,MSFT")]
    public async Task Result_InvalidInput_ShowsTheFieldError_AndKeepsTheInput(string queryString, string field, string? rawValue)
    {
        var form = await FormAsync(queryString);

        form.Error(field).Should().NotBeNullOrEmpty();
        if (rawValue is not null)
        {
            var value = field.StartsWith(BacktestQuery.ParameterPrefix)
                ? form.AllStrategies.Single(s => s.IsSelected).Parameters.Single(p => p.Name == field).Value
                : form.Value(field);
            value.Should().Be(rawValue);
        }
    }

    [Fact] // B: the reason of an unsupported timeframe is given on the strategy field
    public async Task Result_UnsupportedTimeframe_NamesTheRestriction()
    {
        var form = await FormAsync("symbol=AAPL&tf=weekly&strategy=fake-alternating");

        form.Error("strategy").Should().Be("[FAKE] Alternating trades does not support the weekly timeframe (daily only).");
    }

    [Fact] // correction 4: p.fast and P.fast are the same key given twice
    public async Task Result_DuplicateParameter_DifferentCase_IsAnError()
    {
        var form = await FormAsync("symbol=AAPL&strategy=fake-threshold&p.lookback=10&P.lookback=20");

        form.Error("p.lookback").Should().Contain("more than once");
    }

    [Fact] // correction 4: an unknown p.* is an error; with no input to attach it to, it is listed at the top
    public async Task Result_UnknownParameter_IsListedAtTheTop()
    {
        var form = await FormAsync("symbol=AAPL&strategy=fake-threshold&p.speed=3");

        form.OtherErrors.Should().Equal("Unknown parameter 'speed' for this strategy.");
        form.HasErrors.Should().BeTrue();
    }

    // ---- Refused requests (P4-U12): general alert on the form, never a 500 ----

    [Fact] // P4-U12, PART-4 section 7: UNKNOWN
    public async Task Result_SymbolNotFound_ShowsTheMessage()
    {
        var form = await FormAsync(Canonical.Replace("symbol=AAPL", "symbol=UNKNOWN"));

        form.Alert.Should().Be(new FormAlert("Symbol 'UNKNOWN' was not found."));
        form.Value(BacktestQuery.SymbolKey).Should().Be("UNKNOWN");
    }

    [Fact] // P4-U12, PART-4 section 7: LIMIT adds the cached-symbol hint
    public async Task Result_RateLimit_AddsTheHint()
    {
        var form = await FormAsync(Canonical.Replace("symbol=AAPL", "symbol=LIMIT"));

        form.Alert!.Hint.Should().Be("Try a symbol that is already cached, or retry tomorrow.");
        form.Alert.Command.Should().BeNull();
    }

    [Fact] // P4-U12, PART-4 section 7: NOKEY adds the user-secrets command
    public async Task Result_MissingApiKey_AddsTheCommand()
    {
        var form = await FormAsync(Canonical.Replace("symbol=AAPL", "symbol=NOKEY"));

        form.Alert!.Command.Should().StartWith("dotnet user-secrets set \"AlphaVantage:ApiKey\"");
    }

    [Fact] // P4-U12: a BacktestValidationException of the service (no bar in the period) is a general alert
    public async Task Result_ServiceValidationError_ShowsTheMessage()
    {
        var form = await FormAsync(
            "symbol=AAPL&tf=daily&strategy=fake-threshold&from=1990-01-01&to=1990-01-31&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=10&p.threshold=0.02");

        form.Alert.Should().Be(new FormAlert("Not enough bars in the selected period."));
        form.Value(BacktestQuery.FromKey).Should().Be("1990-01-01");
    }

    [Fact] // P4-U12: any other exception is a bug and goes to /Home/Error (not caught here)
    public async Task Result_UnexpectedException_IsNotSwallowed()
    {
        var controller = Controller(Canonical);

        var act = () => controller.Result(new ThrowingBacktestService(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- Result page ----

    [Fact] // PART-4 section 7, P4-U3, P4-U4, P4-U7: valid → result with the metrics, settings, permalink and actions
    public async Task Result_Valid_ShowsTheMetrics_AndThePermalink()
    {
        var view = (await ResultAsync(Canonical)).Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().BeNull();
        var model = view.Model.Should().BeOfType<BacktestResultViewModel>().Subject;

        model.Header.Symbol.Should().Be("AAPL");
        model.Metrics.Should().HaveCount(13);
        model.Metrics[0].Strategy.Should().StartWith("$");
        model.Metrics[3].Label.Should().Be("Average daily return");
        model.Header.Parameters.Should().Equal(new SettingRow("Lookback (bars)", "10"), new SettingRow("Threshold (fraction)", "0.02"));
        model.Header.Permalink.Should().Be("http://localhost:5080" + ResultPath + "?" + Canonical);
        model.Actions.EditSettingsUrl.Should().Be("/Backtest?" + Canonical);
        model.Actions.OptimizeUrl.Should().Be("/Optimize?" + Canonical);
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

    private sealed class ThrowingBacktestService : IBacktestService
    {
        public Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Bug.");
    }
}
