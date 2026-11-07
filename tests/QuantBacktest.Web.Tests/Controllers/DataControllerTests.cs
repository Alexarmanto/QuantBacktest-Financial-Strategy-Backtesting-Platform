using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

public class DataControllerTests
{
    /// <summary>One data service and one TempData shared by the requests of a test, like the app and its cookie.</summary>
    private sealed class Fixture(ScriptedMarketDataService? data = null)
    {
        public ScriptedMarketDataService Data { get; } = data ?? new ScriptedMarketDataService();

        public InMemoryTempDataProvider TempData { get; } = new();

        public DataController Controller(string path, string queryString = "")
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;
            httpContext.Request.QueryString = queryString.Length == 0 ? QueryString.Empty : new QueryString("?" + queryString);
            return new DataController(Data, NullLogger<DataController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, TempData),
            };
        }

        public async Task<DataIndexViewModel> IndexAsync(string queryString = "")
        {
            var controller = Controller("/Data", queryString);
            var view = (await controller.Index(CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
            controller.TempData.Save();
            return view.Model.Should().BeOfType<DataIndexViewModel>().Subject;
        }

        public async Task<IActionResult> QualityAsync(string queryString)
        {
            var controller = Controller("/Data/Quality", queryString);
            var result = await controller.Quality(CancellationToken.None);
            controller.TempData.Save();
            return result;
        }

        public async Task<DataQualityViewModel> QualityViewAsync(string queryString) => QualityModel(await QualityAsync(queryString));

        public async Task<RedirectResult> RefreshAsync(params (string Key, string Value)[] fields)
        {
            var controller = Controller("/Data/Refresh");
            var form = new FormCollection(fields.ToDictionary(f => f.Key, f => new StringValues(f.Value)));
            var result = await controller.Refresh(form, CancellationToken.None);
            controller.TempData.Save();
            return result.Should().BeOfType<RedirectResult>().Subject;
        }
    }

    private static DataQualityViewModel QualityModel(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.StatusCode.Should().BeNull("the page is a normal 200");
        return view.Model.Should().BeOfType<DataQualityViewModel>().Subject;
    }

    /// <summary>The query string of a Location, without the leading "?" and the anchor.</summary>
    private static string QueryOf(string location)
    {
        var query = location[(location.IndexOf('?', StringComparison.Ordinal) + 1)..];
        var anchor = query.IndexOf('#', StringComparison.Ordinal);
        return anchor < 0 ? query : query[..anchor];
    }

    // ---- GET /Data (P4-U10) ----

    [Fact] // P4-U10: one row per cached symbol (service order) and timeframe, daily then weekly
    public async Task Index_ListsEachCachedSymbol_DailyThenWeekly()
    {
        var model = await new Fixture().IndexAsync();

        model.Rows.Select(r => $"{r.Symbol} {r.Timeframe}").Should().Equal(
            "AAPL Daily", "AAPL Weekly", "JPM Daily", "JPM Weekly", "MSFT Daily", "MSFT Weekly", "SPY Daily", "SPY Weekly");
        model.Rows.Should().OnlyContain(r => r.IsCached && r.Anomalies == "2" && r.Origin == "Mixed (seed + API)");
        model.Rows[0].QualityUrl.Should().Be("/Data/Quality?symbol=AAPL&tf=daily");
    }

    [Fact] // P4-U10: cached symbols, then the quality reports one by one, then the quota once; never GetSeriesAsync
    public async Task Index_CallOrder_CachedThenQualityThenQuota_NeverGetSeries()
    {
        var fixture = new Fixture(new ScriptedMarketDataService().WithCachedSymbols("AAPL", "SPY"));

        await fixture.IndexAsync();

        fixture.Data.Calls.Should().Equal(
            "Cached", "Quality AAPL Daily", "Quality AAPL Weekly", "Quality SPY Daily", "Quality SPY Weekly", "Quota");
    }

    [Fact] // P4-U10: NotCached (UNCACHED of the Fakes) → "not cached" with the prefill link, the page still shows
    public async Task Index_UncachedSymbol_NotCachedRows()
    {
        var fixture = new Fixture(new ScriptedMarketDataService().WithCachedSymbols("AAPL", "UNCACHED"));

        var model = await fixture.IndexAsync();

        model.Rows.Where(r => r.Symbol == "UNCACHED").Should().HaveCount(2).And.OnlyContain(r =>
            !r.IsCached && r.Bars == "not cached" && r.QualityUrl == null && r.RefreshFormUrl!.StartsWith("/Data?symbol=UNCACHED&tf="));
        model.Rows.Where(r => r.Symbol == "AAPL").Should().OnlyContain(r => r.IsCached);
    }

    public static TheoryData<Exception> UnexpectedFailures => new()
    {
        new MarketDataException(MarketDataErrorKind.NetworkError, "Network down."),
        new InvalidOperationException("Database locked."),
    };

    [Theory] // P4-U10, P4-U12: only NotCached is absorbed by a row; anything else reaches /Home/Error
    [MemberData(nameof(UnexpectedFailures))]
    public async Task Index_OtherQualityFailure_IsNotSwallowed(Exception failure)
    {
        var fixture = new Fixture(new ScriptedMarketDataService().WithQualityFailure("JPM", Timeframe.Weekly, failure));

        var act = () => fixture.IndexAsync();

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
    }

    [Fact] // P4-U10: an empty cache gives the message and the form, no quality call
    public async Task Index_EmptyCache_MessageAndForm()
    {
        var fixture = new Fixture(new ScriptedMarketDataService().WithCachedSymbols());

        var model = await fixture.IndexAsync();

        model.IsEmpty.Should().BeTrue();
        model.RefreshForm.IsFixedSeries.Should().BeFalse();
        fixture.Data.Calls.Should().Equal("Cached", "Quota");
    }

    [Theory] // P4-U10: the form is prefilled from the URL (tf absent or invalid = daily, unknown keys ignored), no redirect
    [InlineData("", "", "daily")]
    [InlineData("symbol=IBM&tf=weekly&strategy=x", "IBM", "weekly")]
    [InlineData("symbol=ibm&tf=monthly", "ibm", "daily")]
    public async Task Index_PrefillsTheFormFromTheUrl(string queryString, string symbol, string tf)
    {
        var model = await new Fixture().IndexAsync(queryString);

        model.RefreshForm.Value("symbol").Should().Be(symbol);
        model.RefreshForm.Value("tf").Should().Be(tf);
        model.RefreshForm.CachedSymbols.Should().Equal("AAPL", "JPM", "MSFT", "SPY");
    }

    [Fact] // P4-U10: the confirmation uses ApiQuotaStatus.Remaining, read by the server
    public async Task Index_Confirmation_UsesTheRemainingCalls()
    {
        var model = await new Fixture(new ScriptedMarketDataService().WithQuota(usedToday: 25)).IndexAsync();

        model.RefreshForm.Confirmation.Should().Be("This uses one of your daily API calls (0 left today). Continue?");
        model.QuotaUsage.Should().Be("25 / 25");
    }

    // ---- POST /Data/Refresh (P4-U10, PART-4 section 5) ----

    [Fact] // P4-U16: antiforgery token on every POST
    public void Refresh_HasHttpPostAndValidateAntiForgeryToken()
    {
        var method = typeof(DataController).GetMethod(nameof(DataController.Refresh))!;

        method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
    }

    [Theory] // D3: exactly one GetSeriesAsync with forceRefresh, then the canonical quality page (tf absent = daily)
    [InlineData(" ibm ", "WEEKLY", "IBM Weekly", "/Data/Quality?symbol=IBM&tf=weekly")]
    [InlineData("ibm", null, "IBM Daily", "/Data/Quality?symbol=IBM&tf=daily")]
    public async Task Refresh_Valid_OneForcedGetSeries_RedirectsToTheCanonicalQualityPage(
        string symbol, string? tf, string expectedCall, string expectedLocation)
    {
        var fixture = new Fixture();
        var fields = tf is null ? new[] { ("symbol", symbol) } : [("symbol", symbol), ("tf", tf)];

        var redirect = await fixture.RefreshAsync(fields);

        fixture.Data.Calls.Should().Equal($"Series {expectedCall} refresh=True");
        redirect.Url.Should().Be(expectedLocation);
        redirect.Permanent.Should().BeFalse();
    }

    [Fact] // PRG: the Location of a success is canonical (no second redirect) and shows the message once
    public async Task Refresh_Valid_TargetDoesNotRedirectAgain_AndShowsTheNotice()
    {
        var fixture = new Fixture(new ScriptedMarketDataService().WithSeries(SyntheticPrices.Generate("IBM", Timeframe.Daily, 117)));

        var redirect = await fixture.RefreshAsync(("symbol", "IBM"), ("tf", "daily"));
        var model = await fixture.QualityViewAsync(QueryOf(redirect.Url));

        model.Notice.Should().Be(PageNotice.Success("IBM (daily) refreshed from Alpha Vantage: 117 bars in the cache."));
        model.Report.Should().NotBeNull();
        (await fixture.QualityViewAsync(QueryOf(redirect.Url))).Notice.Should().BeNull("the notice is shown once");
    }

    [Theory] // P4-U10: invalid input → no service call, back to the prefilled form with the field error
    [InlineData("", "daily", "/Data?tf=daily#refresh", "symbol", BacktestQuery.SymbolRequiredMessage)]
    [InlineData("bad symbol", "daily", "/Data?symbol=bad%20symbol&tf=daily#refresh", "symbol", BacktestQuery.SymbolFormatMessage)]
    [InlineData("IBM", "monthly", "/Data?symbol=IBM&tf=monthly#refresh", "tf", BacktestQuery.TimeframeFormatMessage)]
    public async Task Refresh_InvalidInput_NoServiceCall_RedirectsToThePrefilledForm(
        string symbol, string tf, string expectedLocation, string field, string message)
    {
        var fixture = new Fixture();

        var redirect = await fixture.RefreshAsync(("symbol", symbol), ("tf", tf));
        fixture.Data.Calls.Should().BeEmpty();
        redirect.Url.Should().Be(expectedLocation);

        var model = await fixture.IndexAsync(QueryOf(redirect.Url));
        model.RefreshForm.Error(field).Should().Be(message);
        model.RefreshForm.Value("symbol").Should().Be(symbol);
        model.RefreshForm.Alert.Should().BeNull();
    }

    [Theory] // P4-U12: refused refresh (Fakes LIMIT, NOKEY, UNKNOWN) → the prefilled form with the alert and its hints
    [InlineData("limit", "LIMIT", FormAlert.RateLimitHint, null)]
    [InlineData("NOKEY", "NOKEY", FormAlert.MissingApiKeyRetryHint, FormAlert.MissingApiKeyCommand)]
    [InlineData("UNKNOWN", "UNKNOWN", null, null)]
    public async Task Refresh_MarketDataError_RedirectsToTheForm_WithTheAlert(string typed, string symbol, string? hint, string? command)
    {
        var fixture = new Fixture();

        var redirect = await fixture.RefreshAsync(("symbol", typed), ("tf", "weekly"));
        redirect.Url.Should().Be($"/Data?symbol={symbol}&tf=weekly#refresh");
        fixture.Data.SeriesCalls.Should().Equal($"Series {symbol} Weekly refresh=True");

        var model = await fixture.IndexAsync(QueryOf(redirect.Url));
        var alert = model.RefreshForm.Alert!;
        alert.Message.Should().NotBeNullOrWhiteSpace();
        alert.Hint.Should().Be(hint);
        alert.Command.Should().Be(command);
        model.RefreshForm.Value("symbol").Should().Be(symbol);
        model.RefreshForm.Value("tf").Should().Be("weekly");
        model.RefreshForm.HasErrors.Should().BeFalse();
    }

    [Fact] // P4-U12: any other exception is a bug, never caught (/Home/Error)
    public async Task Refresh_UnexpectedException_IsNotSwallowed()
    {
        var failure = new InvalidOperationException("Database locked.");
        var fixture = new Fixture(new ScriptedMarketDataService().WithSeriesFailure(failure));

        var act = () => fixture.RefreshAsync(("symbol", "IBM"), ("tf", "daily"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    // ---- GET /Data/Quality (P4-U10) ----

    [Fact] // P4-U10: every field of the report and its anomalies; the refresh form for this series; no GetSeriesAsync
    public async Task Quality_Valid_EveryFieldAndAnomalies_NeverGetSeries()
    {
        var anomaly = new DataAnomaly(new DateOnly(2008, 3, 20), DataAnomalyKind.PriceJump, "Close changed by +25.8 % in one bar (possible split or bad tick).");
        var fixture = new Fixture(new ScriptedMarketDataService().WithReport(DataViewModelTests.Report("JPM", Timeframe.Weekly, anomaly)).WithQuota(3));

        var model = await fixture.QualityViewAsync("symbol=JPM&tf=weekly");

        model.Heading.Should().Be("Data quality: JPM weekly");
        model.Report!.Bars.Should().Be("1,406");
        model.Report.LastUpdated.Should().Be("2026-11-06 23:03 UTC");
        model.Report.Anomalies.Single().Description.Should().Be(anomaly.Description);
        model.RefreshForm!.IsFixedSeries.Should().BeTrue();
        model.RefreshForm.Value("symbol").Should().Be("JPM");
        model.RefreshForm.Value("tf").Should().Be("weekly");
        model.RefreshForm.Confirmation.Should().Be("This uses one of your daily API calls (22 left today). Continue?");
        model.Alert.Should().BeNull();
        model.BackUrl.Should().Be("/Data");
        fixture.Data.Calls.Should().Equal("Quality JPM Weekly", "Quota");
    }

    [Fact] // P4-U10: a report without anomaly
    public async Task Quality_NoAnomaly_EmptyAnomalies()
    {
        var fixture = new Fixture(new ScriptedMarketDataService().WithReport(DataViewModelTests.Report("AAPL", Timeframe.Daily)));

        var model = await fixture.QualityViewAsync("symbol=AAPL&tf=daily");

        model.Report!.Anomalies.Should().BeEmpty();
    }

    [Fact] // D7: NotCached (UNCACHED) → alert with the service's message and the refresh form, status 200, no exception
    public async Task Quality_Uncached_NotCachedAlert_AndTheRefreshForm()
    {
        var fixture = new Fixture();

        var model = await fixture.QualityViewAsync("symbol=UNCACHED&tf=weekly");

        model.Alert.Should().Be(new FormAlert("No cached data for UNCACHED (Weekly)."));
        model.Report.Should().BeNull();
        model.RefreshForm!.Value("symbol").Should().Be("UNCACHED");
        model.RefreshForm.Value("tf").Should().Be("weekly");
        fixture.Data.SeriesCalls.Should().BeEmpty();
    }

    [Fact] // P4-U12: another MarketDataException is an alert (FormAlert), not an error page
    public async Task Quality_OtherMarketDataException_Alert()
    {
        var fixture = new Fixture(new ScriptedMarketDataService()
            .WithQualityFailure("AAPL", Timeframe.Daily, new MarketDataException(MarketDataErrorKind.InvalidResponse, "Corrupted cache.")));

        var model = await fixture.QualityViewAsync("symbol=AAPL&tf=daily");

        model.Alert.Should().Be(new FormAlert("Corrupted cache."));
        model.Report.Should().BeNull();
    }

    [Fact] // P4-U12: any other exception is a bug, never caught
    public async Task Quality_UnexpectedException_IsNotSwallowed()
    {
        var failure = new InvalidOperationException("Database locked.");
        var fixture = new Fixture(new ScriptedMarketDataService().WithQualityFailure("AAPL", Timeframe.Daily, failure));

        var act = () => fixture.QualityAsync("symbol=AAPL&tf=daily");

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Theory] // P4-U10: symbol absent or invalid, tf present but invalid → the errors and the back link, no service call
    [InlineData("", BacktestQuery.SymbolRequiredMessage)]
    [InlineData("tf=daily", BacktestQuery.SymbolRequiredMessage)]
    [InlineData("symbol=&tf=", BacktestQuery.SymbolRequiredMessage)]
    [InlineData("symbol=BAD%20SYMBOL!&tf=daily", BacktestQuery.SymbolFormatMessage)]
    [InlineData("symbol=AAPL&tf=monthly", BacktestQuery.TimeframeFormatMessage)]
    public async Task Quality_InvalidLink_ErrorsWithoutAnyServiceCall(string queryString, string message)
    {
        var fixture = new Fixture();

        var model = await fixture.QualityViewAsync(queryString);

        model.InputErrors.Should().Equal(message);
        model.Report.Should().BeNull();
        model.Alert.Should().BeNull();
        model.RefreshForm.Should().BeNull();
        model.BackUrl.Should().Be("/Data");
        fixture.Data.Calls.Should().BeEmpty();
    }

    [Theory] // PART-4 section 5: one redirect to the canonical URL (tf absent → daily), no service call before it
    [InlineData("symbol=aapl&tf=daily", "/Data/Quality?symbol=AAPL&tf=daily")]
    [InlineData("symbol=AAPL", "/Data/Quality?symbol=AAPL&tf=daily")]
    [InlineData("symbol=AAPL&tf=WEEKLY", "/Data/Quality?symbol=AAPL&tf=weekly")]
    [InlineData("tf=daily&symbol=AAPL&x=1", "/Data/Quality?symbol=AAPL&tf=daily")]
    public async Task Quality_NonCanonical_RedirectsOnceToTheCanonicalUrl(string queryString, string expectedLocation)
    {
        var fixture = new Fixture();

        var redirect = (await fixture.QualityAsync(queryString)).Should().BeOfType<RedirectResult>().Subject;

        redirect.Url.Should().Be(expectedLocation);
        fixture.Data.Calls.Should().BeEmpty();
        (await fixture.QualityAsync(QueryOf(redirect.Url))).Should().BeOfType<ViewResult>("the canonical URL never redirects");
    }

    [Fact] // P4-U16: the two pages are GET; looking at them never costs an API call
    public void IndexAndQuality_AreHttpGet()
    {
        typeof(DataController).GetMethod(nameof(DataController.Index))!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
        typeof(DataController).GetMethod(nameof(DataController.Quality))!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull();
    }

    [Fact] // P4-U13: /Data/Quality is an action of DataController, so the layout highlights the Data link (controller match)
    public void Quality_IsOnTheDataController_SoTheNavigationHighlightsData()
    {
        typeof(DataController).Name.Should().Be("DataController");
        QuantBacktest.Web.Infrastructure.Navigation.Items.Should().ContainSingle(i => i.Controller == "Data" && i.Enabled);
    }
}
