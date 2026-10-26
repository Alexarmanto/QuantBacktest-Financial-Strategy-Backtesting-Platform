using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class BacktestFormViewModelTests
{
    private static readonly StubStrategy Crossover = new()
    {
        Id = "sma-crossover",
        Name = "Moving average crossover",
        Parameters =
        [
            new ParameterDefinition("fast", "Fast period", ParameterType.Integer, 20, 2, 200, 1, "Short average."),
            new ParameterDefinition("slow", "Slow period", ParameterType.Integer, 50, 5, 400, 1),
        ],
    };

    private static readonly StubStrategy DailyOnly = new()
    {
        Id = "turn-of-month",
        Name = "Turn of month",
        SupportedTimeframes = [Timeframe.Daily],
        Parameters = [new ParameterDefinition("daysBeforeEnd", "Days before end", ParameterType.Integer, 1, 1, 5, 1)],
    };

    private static readonly StubStrategy Custom = new() { Id = "custom-dip", Name = "Dip", Category = StrategyCategory.Custom };

    private static BacktestFormViewModel Create(string queryString, params QueryFieldError[] errors) =>
        BacktestFormViewModel.Create(
            new QueryCollection(QueryHelpers.ParseQuery(queryString)),
            [Crossover, DailyOnly, Custom],
            ["MSFT", "SPY"],
            errors,
            alert: null);

    [Fact] // P4-U1: built-in strategies first, then custom ones
    public void Strategies_AreGrouped_BuiltInThenCustom()
    {
        var form = Create("");

        form.BuiltInStrategies.Select(s => s.Id).Should().Equal("sma-crossover", "turn-of-month");
        form.CustomStrategies.Select(s => s.Id).Should().Equal("custom-dip");
    }

    [Fact] // P4-U1: parameter inputs from the definitions (default, min, max, step, description)
    public void ParameterFields_ComeFromTheDefinitions()
    {
        var fast = Create("").AllStrategies.First().Parameters[0];

        fast.Should().Be(new ParameterField("p.fast", "Fast period", "20", "2", "200", "1", "Short average.", null));
    }

    [Fact] // P4-U12: the raw value of the selected strategy is kept; other strategies show their defaults
    public void RawParameterValues_AreKept_ForTheSelectedStrategyOnly()
    {
        var form = Create("strategy=turn-of-month&p.daysBeforeEnd=abc&p.fast=7");

        form.AllStrategies.Single(s => s.Id == "turn-of-month").Parameters[0].Value.Should().Be("abc");
        form.AllStrategies.Single(s => s.Id == "sma-crossover").Parameters[0].Value.Should().Be("20");
    }

    [Fact] // P4-U12: each error under its field, keyed by URL name, case-insensitive like the query
    public void Errors_AreAttachedToTheirField()
    {
        var form = Create(
            "strategy=sma-crossover&capital=-5&P.fast=1",
            new QueryFieldError("capital", "Initial capital must be greater than 0."),
            new QueryFieldError("P.fast", "Fast period must be between 2 and 200."));

        form.Error("capital").Should().Be("Initial capital must be greater than 0.");
        form.AllStrategies.First().Parameters[0].Error.Should().Be("Fast period must be between 2 and 200.");
        form.AllStrategies.First().Parameters[0].Value.Should().Be("1");
        form.OtherErrors.Should().BeEmpty();
    }

    [Fact] // P4-U12: an error without an input on the page is not lost
    public void ErrorsWithoutAField_AreListedAtTheTop()
    {
        var form = Create(
            "strategy=sma-crossover&p.speed=3&p.=1",
            new QueryFieldError("p.speed", "Unknown parameter 'speed' for this strategy."),
            new QueryFieldError("p.", "A parameter name is missing after 'p.'."));

        form.OtherErrors.Should().Equal("Unknown parameter 'speed' for this strategy.", "A parameter name is missing after 'p.'.");
        form.HasErrors.Should().BeTrue();
    }

    [Fact] // decision E: blank form gets the first cached symbol; a sent form keeps the symbol as typed (even empty)
    public void DefaultSymbol_OnlyOnABlankForm()
    {
        Create("").Value("symbol").Should().Be("MSFT");
        Create("strategy=sma-crossover").Value("symbol").Should().BeEmpty();
    }

    [Fact] // P4-U1: a daily-only strategy carries its restriction for setCustomValidity
    public void Restriction_IsGiven_ForADailyOnlyStrategy()
    {
        var option = Create("").AllStrategies.Single(s => s.Id == "turn-of-month");

        option.Timeframes.Should().Be("daily");
        option.Restriction.Should().Be("daily only");
    }

    [Theory] // decision C, P4-U1: the client pattern accepts what the server accepts after upper-casing
    [InlineData("aapl", true)]
    [InlineData("BRK.B", true)]
    [InlineData("^GSPC", false)]
    [InlineData("TOOLONGSYMB", false)]
    public void SymbolInputPattern_MatchesTheServerRule(string symbol, bool valid)
    {
        var client = Regex.IsMatch(symbol, $"^(?:{BacktestFormViewModel.SymbolInputPattern})$");
        var server = Regex.IsMatch(symbol.ToUpperInvariant(), Conventions.SymbolPattern);

        client.Should().Be(valid).And.Be(server);
    }

    [Fact] // P4-U12: LIMIT and NOKEY complements
    public void Alert_FromMarketDataErrors_HasTheComplements()
    {
        FormAlert.From(new MarketDataException(MarketDataErrorKind.RateLimitExceeded, "Limit."))
            .Should().Be(new FormAlert("Limit.", FormAlert.RateLimitHint));
        FormAlert.From(new MarketDataException(MarketDataErrorKind.MissingApiKey, "No key."))
            .Should().Be(new FormAlert("No key.", FormAlert.MissingApiKeyHint, FormAlert.MissingApiKeyCommand));
        FormAlert.From(new MarketDataException(MarketDataErrorKind.NetworkError, "Network."))
            .Should().Be(new FormAlert("Network."));
    }
}
