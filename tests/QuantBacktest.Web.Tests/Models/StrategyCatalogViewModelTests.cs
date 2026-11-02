using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class StrategyCatalogViewModelTests
{
    private static readonly StubStrategy Threshold = new()
    {
        Id = "momentum-roc",
        Name = "Momentum (rate of change)",
        Description = "Buys strength.",
        Category = StrategyCategory.Momentum,
        Documentation = new StrategyDocumentation("The idea.", "The entry.", "The exit.", "The risks."),
        SupportedTimeframes = [Timeframe.Weekly, Timeframe.Daily],
        Parameters =
        [
            new ParameterDefinition("lookback", "Lookback (bars)", ParameterType.Integer, 14, 2, 250, 1),
            new ParameterDefinition("threshold", "Threshold (fraction)", ParameterType.Decimal, 0.02, 0, 0.5, 0.005, "0.02 means 2 %."),
        ],
    };

    private static readonly StubStrategy Custom = new()
    {
        Id = "custom-trend-dip",
        Name = "Trend dip",
        Category = StrategyCategory.Custom,
    };

    private static readonly StubStrategy BuiltIn = new() { Id = "buy-and-hold", Name = "Buy and hold" };

    [Fact] // P4-U8: Idea, Entry rule, Exit rule and Risks come from Documentation as they are
    public void Card_CopiesTheDocumentation()
    {
        var card = StrategyCardViewModel.Create(Threshold, pathBase: "");

        card.Name.Should().Be("Momentum (rate of change)");
        card.Description.Should().Be("Buys strength.");
        card.Documentation.Should().Be(new StrategyDocumentation("The idea.", "The entry.", "The exit.", "The risks."));
    }

    [Fact] // P4-U8: one row per parameter, numbers in their invariant form, whatever the culture of the thread
    public void Card_FormatsTheParameters_InvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var card = StrategyCardViewModel.Create(Threshold, pathBase: "");

            card.Parameters.Should().Equal(
                new ParameterRow("Lookback (bars)", "Integer", "14", "2", "250", "1", null),
                new ParameterRow("Threshold (fraction)", "Decimal", "0.02", "0", "0.5", "0.005", "0.02 means 2 %."));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact] // P4-U8: a strategy without parameters (buy-and-hold, custom R8) has an empty table
    public void Card_WithoutParameters_HasNoRows()
    {
        StrategyCardViewModel.Create(BuiltIn, pathBase: "").Parameters.Should().BeEmpty();
    }

    [Fact] // P4-U8: supported timeframes, in enum order, readable
    public void Card_ListsTheTimeframes()
    {
        StrategyCardViewModel.Create(Threshold, pathBase: "").Timeframes.Should().Equal("Daily", "Weekly");
    }

    [Fact] // P4-U8, decision 5: every category has a readable label, never empty nor the raw enum name
    public void EveryCategory_HasAReadableLabel()
    {
        foreach (var category in Enum.GetValues<StrategyCategory>())
        {
            var label = StrategyCardViewModel.CategoryText(category);

            label.Should().NotBeNullOrWhiteSpace();

            // A one-word name ("Momentum") is already readable; a raw PascalCase name ("TrendFollowing") is not.
            label.Should().NotMatchRegex("[a-z][A-Z]", "category {0} needs a readable label", category);
        }

        StrategyCardViewModel.CategoryText(StrategyCategory.TrendFollowing).Should().NotBe(nameof(StrategyCategory.TrendFollowing));
        FluentActions.Invoking(() => StrategyCardViewModel.CategoryText((StrategyCategory)99)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact] // P4-U8: card category label
    public void Card_HasTheCategoryLabel()
    {
        StrategyCardViewModel.Create(Threshold, pathBase: "").CategoryLabel.Should().Be("Momentum");
        StrategyCardViewModel.CategoryText(StrategyCategory.TrendFollowing).Should().Be("Trend following");
    }

    [Fact] // P4-U8, P4-U16: "Use this strategy" opens the form through BacktestQuery, path base kept
    public void UseUrl_OpensTheFormWithTheStrategy()
    {
        StrategyCardViewModel.Create(Threshold, pathBase: "").UseUrl.Should().Be("/Backtest?strategy=momentum-roc");
        StrategyCardViewModel.Create(Threshold, pathBase: "/app").UseUrl.Should().Be("/app/Backtest?strategy=momentum-roc");
    }

    [Fact] // P4-U8, decision 4: a weekly-only strategy opens the form on weekly
    public void UseUrl_ForAWeeklyOnlyStrategy_SelectsWeekly()
    {
        var weeklyOnly = new StubStrategy { Id = "weekly-only", SupportedTimeframes = [Timeframe.Weekly] };

        StrategyCardViewModel.Create(weeklyOnly, pathBase: "").UseUrl.Should().Be("/Backtest?strategy=weekly-only&tf=weekly");
    }

    [Fact] // P4-U8, PART-4 section 5: Edit and Delete for custom strategies only, on the fixed routes
    public void EditAndDelete_OnlyForCustomStrategies()
    {
        var custom = StrategyCardViewModel.Create(Custom, pathBase: "/app");
        custom.IsCustom.Should().BeTrue();
        custom.EditUrl.Should().Be("/app/Strategies/Custom/custom-trend-dip/Edit");
        custom.DeleteUrl.Should().Be("/app/Strategies/Custom/custom-trend-dip/Delete");
        custom.DeleteConfirmation.Should().Be("Delete the strategy \"Trend dip\"? This cannot be undone.");

        var builtIn = StrategyCardViewModel.Create(BuiltIn, pathBase: "");
        builtIn.IsCustom.Should().BeFalse();
        builtIn.EditUrl.Should().BeNull();
        builtIn.DeleteUrl.Should().BeNull();
    }

    [Fact] // P4-U8: each card has an anchor for the user manual
    public void Card_HasAnAnchor()
    {
        StrategyCardViewModel.Create(Threshold, pathBase: "").Anchor.Should().Be("strategy-momentum-roc");
    }

    [Fact] // P4-U8, P4-U1: built-in first, then custom, each group in catalog order (same grouping as the form)
    public void Catalog_GroupsBuiltInThenCustom_InCatalogOrder()
    {
        var model = StrategyCatalogViewModel.Create([Custom, Threshold, BuiltIn], notice: null, pathBase: "");

        model.BuiltInStrategies.Select(c => c.Id).Should().Equal("momentum-roc", "buy-and-hold");
        model.CustomStrategies.Select(c => c.Id).Should().Equal("custom-trend-dip");
        model.Notice.Should().BeNull();
    }

    [Fact] // P4-U8: in-page navigation counts each section, distinct anchors, never colliding with a card anchor
    public void Navigation_CountsEachSection()
    {
        var model = StrategyCatalogViewModel.Create([Custom, Threshold, BuiltIn], notice: null, pathBase: "");

        model.BuiltInCount.Should().Be(2);
        model.CustomCount.Should().Be(1);
        model.BuiltInLinkText.Should().Be("Built-in (2)");
        model.CustomLinkText.Should().Be("Custom (1)");
        StrategyCatalogViewModel.BuiltInAnchor.Should().Be("built-in");
        StrategyCatalogViewModel.CustomAnchor.Should().Be("custom");
        StrategyCatalogViewModel.BuiltInAnchor.Should().NotStartWith("strategy-");
        StrategyCatalogViewModel.CustomAnchor.Should().NotStartWith("strategy-");
    }

    [Fact] // P4-U8: without custom strategy the link stays, at zero
    public void Navigation_KeepsTheCustomLink_AtZero()
    {
        var model = StrategyCatalogViewModel.Create([Threshold, BuiltIn], notice: null, pathBase: "");

        model.CustomCount.Should().Be(0);
        model.CustomLinkText.Should().Be("Custom (0)");
    }

    [Theory] // P4-U8: the message of a deletion survives the redirect through TempData, once
    [InlineData(true)]
    [InlineData(false)]
    public void PageNotice_RoundTripsThroughTempData(bool success)
    {
        var provider = new InMemoryTempDataProvider();
        TempDataDictionary Request() => new(new DefaultHttpContext(), provider);
        var notice = success ? PageNotice.Success("Done.") : PageNotice.Error("Refused.");

        var post = Request();
        notice.WriteTo(post);
        post.Save();

        var redirected = Request();
        PageNotice.ReadFrom(redirected).Should().Be(notice);
        redirected.Save();

        PageNotice.ReadFrom(Request()).Should().BeNull();
    }
}
