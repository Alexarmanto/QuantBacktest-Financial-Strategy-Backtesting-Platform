using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

public class StrategiesApiControllerTests
{
    /// <summary>Runs the action and serializes its result with the real MVC JSON executor (default options of the app).</summary>
    private static async Task<(string ContentType, JsonArray Body)> GetAsync(IStrategyCatalog catalog)
    {
        var result = await new StrategiesApiController(catalog).Index(CancellationToken.None);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await result.ExecuteResultAsync(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));

        body.Position = 0;
        var json = await new StreamReader(body).ReadToEndAsync();
        return (httpContext.Response.ContentType!, JsonNode.Parse(json)!.AsArray());
    }

    /// <summary>Structural comparison: independent of whitespace, encoder escaping and property order.</summary>
    private static void ShouldMatch(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue("actual JSON was {0}", actual?.ToJsonString());

    [Fact] // PART-4 section 5, route GET /api/strategies
    public void Route_IsApiStrategies()
    {
        typeof(StrategiesApiController).GetMethod(nameof(StrategiesApiController.Index))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("/api/strategies");
    }

    [Fact] // PART-4 section 5: exact JSON shape, camelCase, lower-case timeframes and type
    public async Task Json_HasTheExactShape()
    {
        var catalog = new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());

        var (contentType, body) = await GetAsync(catalog);

        contentType.Should().StartWith("application/json");
        var alternating = body.Single(s => (string?)s!["id"] == "fake-alternating")!;
        ShouldMatch(alternating, """
            {
              "id": "fake-alternating",
              "name": "[FAKE] Alternating trades",
              "description": "Placeholder: buys and sells at fixed intervals. Daily only. Not a real strategy.",
              "category": "momentum",
              "supportedTimeframes": ["daily"],
              "parameters": [
                { "key": "period", "label": "Period (bars)", "type": "integer", "default": 20, "min": 2, "max": 200, "step": 1,
                  "description": "Bars between a buy and the next sell." },
                { "key": "offset", "label": "Offset (bars)", "type": "integer", "default": 0, "min": 0, "max": 50, "step": 1,
                  "description": "Shifts the rhythm." }
              ],
              "documentation": {
                "idea": "[FAKE] Trades on a fixed rhythm to exercise the UI.",
                "entryRule": "Buy every 2 x period bars.",
                "exitRule": "Sell period bars after each buy.",
                "risks": "Meaningless: placeholder only."
              }
            }
            """);
    }

    [Fact] // PART-4 section 5: every strategy, in catalog order (built-in then custom, CONTRACT IStrategyCatalog)
    public async Task Json_ListsTheCatalog_InCatalogOrder()
    {
        var catalog = new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory());
        var expectedIds = (await catalog.GetAllAsync()).Select(s => s.Id).ToList();

        var (_, body) = await GetAsync(catalog);

        body.Select(s => (string?)s!["id"]).Should().Equal(expectedIds);
        body.Should().Contain(s => (string?)s!["category"] == "custom");
    }

    [Fact] // PART-4 sections 3 and 7: a strategy added to the catalog appears without any change to the web
    public async Task NewStrategy_InTheCatalog_AppearsAsIs()
    {
        var added = new StubStrategy
        {
            Id = "test-breakout",
            Name = "Test breakout",
            Description = "Added only to the test catalog.",
            Category = StrategyCategory.TrendFollowing,
            Documentation = new StrategyDocumentation("I.", "E.", "X.", "R."),
            SupportedTimeframes = [Timeframe.Weekly],
            Parameters = [new ParameterDefinition("level", "Level", ParameterType.Decimal, 0.25, 0.005, 1.5, 0.005)],
        };
        var catalog = new StubStrategyCatalog(new StubStrategy { Id = "first" }, added);

        var (_, body) = await GetAsync(catalog);

        body.Select(s => (string?)s!["id"]).Should().Equal("first", "test-breakout");
        ShouldMatch(body[1], """
            {
              "id": "test-breakout",
              "name": "Test breakout",
              "description": "Added only to the test catalog.",
              "category": "trendFollowing",
              "supportedTimeframes": ["weekly"],
              "parameters": [
                { "key": "level", "label": "Level", "type": "decimal", "default": 0.25, "min": 0.005, "max": 1.5, "step": 0.005,
                  "description": null }
              ],
              "documentation": { "idea": "I.", "entryRule": "E.", "exitRule": "X.", "risks": "R." }
            }
            """);
        body[0]!["parameters"]!.AsArray().Should().BeEmpty();
    }
}
