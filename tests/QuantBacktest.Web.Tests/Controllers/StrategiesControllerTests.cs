using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

public class StrategiesControllerTests
{
    private const string ExampleId = "custom-trend-dip";

    /// <summary>The fakes of the app: two built-in placeholders and the example custom strategy of the store.</summary>
    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new RecordingCustomStrategyStore(new FakeCustomStrategyStore());
            Catalog = new FakeStrategyCatalog(Store, new FakeCustomStrategyFactory());
        }

        public RecordingCustomStrategyStore Store { get; }

        public FakeStrategyCatalog Catalog { get; }

        /// <summary>Shared by every controller of the fixture, like the TempData cookie between two requests.</summary>
        public InMemoryTempDataProvider TempData { get; } = new();

        /// <summary>One request: a new controller with the TempData saved by the previous one.</summary>
        public StrategiesController Controller()
        {
            var httpContext = new DefaultHttpContext();
            return new StrategiesController(Catalog, Store, NullLogger<StrategiesController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, TempData),
            };
        }

        /// <summary>POST then the GET of the redirect, as the browser does.</summary>
        public async Task<StrategyCatalogViewModel> DeleteThenFollowAsync(string id)
        {
            var post = Controller();
            var result = await post.Delete(id, CancellationToken.None);
            post.TempData.Save();

            var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
            redirect.ActionName.Should().Be(nameof(StrategiesController.Index));
            redirect.ControllerName.Should().BeNull();

            return await IndexAsync();
        }

        public async Task<StrategyCatalogViewModel> IndexAsync()
        {
            var controller = Controller();
            var view = (await controller.Index(CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
            controller.TempData.Save();
            return view.Model.Should().BeOfType<StrategyCatalogViewModel>().Subject;
        }
    }

    private static IEnumerable<string> Ids(StrategyCatalogViewModel model) =>
        model.BuiltInStrategies.Concat(model.CustomStrategies).Select(c => c.Id);

    // ---- GET /Strategies (P4-U8) ----

    [Fact] // P4-U8: one card per strategy of the catalog, nothing hard-coded
    public async Task Index_ListsEveryCatalogStrategy()
    {
        var fixture = new Fixture();
        var expected = await fixture.Catalog.GetAllAsync();

        var model = await fixture.IndexAsync();

        Ids(model).Should().Equal(expected.Select(s => s.Id));
        model.CustomStrategies.Select(c => c.Id).Should().Equal(ExampleId);
        model.Notice.Should().BeNull();
    }

    [Fact] // P4-U8, P4-U1: built-in first, then custom, each in catalog order, whatever order the catalog mixes them in
    public async Task Index_GroupsBuiltInThenCustom()
    {
        var catalog = new StubStrategyCatalog(
            new StubStrategy { Id = "custom-b", Category = StrategyCategory.Custom },
            new StubStrategy { Id = "z-builtin" },
            new StubStrategy { Id = "custom-a", Category = StrategyCategory.Custom },
            new StubStrategy { Id = "a-builtin" });
        var httpContext = new DefaultHttpContext();
        var controller = new StrategiesController(catalog, new FakeCustomStrategyStore(), NullLogger<StrategiesController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider()),
        };

        var view = (await controller.Index(CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
        var model = view.Model.Should().BeOfType<StrategyCatalogViewModel>().Subject;

        model.BuiltInStrategies.Select(c => c.Id).Should().Equal("z-builtin", "a-builtin");
        model.CustomStrategies.Select(c => c.Id).Should().Equal("custom-b", "custom-a");
    }

    // ---- POST /Strategies/Custom/{id}/Delete (PART-4 section 5, P4-U8) ----

    [Fact] // PART-4 section 5, P4-U16: fixed POST route, antiforgery token required
    public void Delete_IsAPostOnTheFixedRoute_WithAntiforgery()
    {
        var method = typeof(StrategiesController).GetMethod(nameof(StrategiesController.Delete))!;

        method.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("/Strategies/Custom/{id}/Delete");
        method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
        method.GetCustomAttribute<HttpGetAttribute>().Should().BeNull();
    }

    [Fact] // P4-U8: deleting a custom strategy removes it and redirects to the catalog with a success message
    public async Task Delete_Custom_DeletesAndRedirectsWithSuccess()
    {
        var fixture = new Fixture();

        var model = await fixture.DeleteThenFollowAsync(ExampleId);

        fixture.Store.DeletedIds.Should().Equal(ExampleId);
        (await fixture.Store.GetAsync(ExampleId)).Should().BeNull();
        model.CustomStrategies.Should().BeEmpty();
        model.Notice.Should().Be(PageNotice.Success("The strategy \"Trend dip\" was deleted."));
    }

    [Fact] // P4-U8: the message is shown once, not on the next visit
    public async Task Delete_Message_IsShownOnce()
    {
        var fixture = new Fixture();
        await fixture.DeleteThenFollowAsync(ExampleId);

        (await fixture.IndexAsync()).Notice.Should().BeNull();
    }

    [Theory] // P4-U8: unknown id → error message on the catalog, never an exception
    [InlineData("custom-does-not-exist")]
    [InlineData("does-not-exist")]
    [InlineData("Not A Valid Id")]
    public async Task Delete_UnknownId_RedirectsWithAnError(string id)
    {
        var fixture = new Fixture();

        var model = await fixture.DeleteThenFollowAsync(id);

        model.Notice.Should().Be(PageNotice.Error(StrategiesController.NotFoundMessage(id)));
        model.CustomStrategies.Select(c => c.Id).Should().Equal(ExampleId);
    }

    [Fact] // P4-U8, decision 2: an id outside the custom pattern never reaches the store
    public async Task Delete_UnknownNonCustomId_DoesNotCallTheStore()
    {
        var fixture = new Fixture();

        await fixture.DeleteThenFollowAsync("does-not-exist");

        fixture.Store.DeletedIds.Should().BeEmpty();
    }

    [Fact] // P4-U8, decision 2: a built-in strategy is refused, the store and the catalog are unchanged
    public async Task Delete_BuiltIn_IsRefused()
    {
        var fixture = new Fixture();
        var before = (await fixture.Catalog.GetAllAsync()).Select(s => s.Id).ToList();

        var model = await fixture.DeleteThenFollowAsync("fake-threshold");

        model.Notice.Should().Be(PageNotice.Error(StrategiesController.BuiltInRefused));
        fixture.Store.DeletedIds.Should().BeEmpty();
        Ids(model).Should().Equal(before);
    }

    [Fact] // P4-U12: a permalink to a deleted strategy shows the form with a clear error, not a 500
    public async Task Result_ForADeletedStrategy_ShowsTheFormWithAnError()
    {
        var fixture = new Fixture();
        await fixture.DeleteThenFollowAsync(ExampleId);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/Backtest/Result";
        httpContext.Request.QueryString = new QueryString(
            "?symbol=AAPL&tf=daily&strategy=custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0");
        var marketData = new FakeMarketDataService();
        var backtest = new BacktestController(fixture.Catalog, marketData, NullLogger<BacktestController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var result = await backtest.Result(new FakeBacktestService(marketData, fixture.Catalog), CancellationToken.None);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Index");
        var form = view.Model.Should().BeOfType<BacktestFormViewModel>().Subject;
        form.Error(BacktestQuery.StrategyKey).Should().Be("Unknown strategy 'custom-trend-dip'.");
    }
}
