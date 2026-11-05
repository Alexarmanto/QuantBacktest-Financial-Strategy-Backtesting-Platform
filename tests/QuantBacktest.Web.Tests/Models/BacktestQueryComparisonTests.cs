using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class BacktestQueryComparisonTests
{
    private const string MinimalCanonical =
        "symbol=AAPL&tf=daily&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0";

    private const string FullCanonical =
        "symbol=AAPL&tf=weekly&strategies=sma-crossover,rsi-reversion,buy-and-hold&from=2020-01-02&to=2024-12-31"
        + "&capital=25000.5&fee=0.25&slip=0.05&sl=5&tp=12.5&ts=3&rf=2";

    private static IQueryCollection Query(string queryString) => new QueryCollection(QueryHelpers.ParseQuery(queryString));

    private static ComparisonQuery Parse(string queryString)
    {
        BacktestQuery.TryParseComparison(Query(queryString), out var result, out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        return result!;
    }

    private static IReadOnlyList<QueryFieldError> Errors(string queryString)
    {
        BacktestQuery.TryParseComparison(Query(queryString), out var result, out var errors).Should().BeFalse();
        result.Should().BeNull();
        return errors;
    }

    private static readonly StubStrategy Threshold = new()
    {
        Id = "fake-threshold",
        Parameters =
        [
            new ParameterDefinition("lookback", "Lookback", ParameterType.Integer, 10, 1, 100, 1),
            new ParameterDefinition("threshold", "Threshold", ParameterType.Decimal, 0.02, 0, 0.5, 0.005),
        ],
    };

    [Theory] // P4-X1, PART-4 section 5: URL → model → identical URL
    [InlineData(MinimalCanonical)]
    [InlineData(FullCanonical)]
    public void TryParseComparison_CanonicalUrl_RoundTrips(string canonical)
    {
        var query = Parse(canonical);

        query.ToQueryString().Should().Be(canonical);
        query.IsCanonical(new QueryString("?" + canonical)).Should().BeTrue();
    }

    [Fact] // P4-X1: a GET form sends one strategies pair per checked box; values are merged in the order received
    public void TryParseComparison_RepeatedKeysAndCommas_MergeInOrder()
    {
        var query = Parse("symbol=AAPL&strategies=b&strategies=a,c");

        query.StrategyIds.Should().Equal("b", "a", "c");
    }

    [Theory] // P4-X1: the ids keep the order of the user, they are never sorted
    [InlineData("strategies=custom-trend-dip,fake-threshold", new[] { "custom-trend-dip", "fake-threshold" })]
    [InlineData("strategies=fake-threshold,custom-trend-dip", new[] { "fake-threshold", "custom-trend-dip" })]
    public void TryParseComparison_KeepsTheUserOrder(string strategies, string[] expected)
    {
        Parse("symbol=AAPL&" + strategies).StrategyIds.Should().Equal(expected);
    }

    [Fact] // P4-X1: empty items count as absent, like an empty field; the redirect drops them
    public void TryParseComparison_EmptyItems_AreIgnored()
    {
        var query = Parse("symbol=AAPL&strategies=a,, b ,&strategies=");

        query.StrategyIds.Should().Equal("a", "b");
        query.IsCanonical(new QueryString("?symbol=AAPL&strategies=a,, b ,&strategies=")).Should().BeFalse();
    }

    [Theory] // C1: at least 2 strategies, counted after the empty items are dropped
    [InlineData("symbol=AAPL")]
    [InlineData("symbol=AAPL&strategies=")]
    [InlineData("symbol=AAPL&strategies=a")]
    [InlineData("symbol=AAPL&strategies=a,,")]
    public void TryParseComparison_FewerThanTwo_IsFieldError(string queryString)
    {
        Errors(queryString).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(BacktestQuery.StrategiesKey, "Select at least 2 strategies to compare."));
    }

    [Fact] // C1: at most Conventions.MaxComparedStrategies strategies
    public void TryParseComparison_MoreThanMax_IsFieldError()
    {
        Conventions.MaxComparedStrategies.Should().Be(5);
        var ids = string.Join(',', Enumerable.Range(1, Conventions.MaxComparedStrategies + 1).Select(i => $"s{i}"));

        Errors("symbol=AAPL&strategies=" + ids).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(BacktestQuery.StrategiesKey, "Select at most 5 strategies to compare."));
    }

    [Fact] // C1: exactly Max strategies is accepted
    public void TryParseComparison_ExactlyMax_IsAccepted()
    {
        Parse("symbol=AAPL&strategies=a,b,c,d,e").StrategyIds.Should().HaveCount(Conventions.MaxComparedStrategies);
    }

    [Theory] // P4-X1: a strategy given twice is an error, never silently merged
    [InlineData("symbol=AAPL&strategies=a,a")]
    [InlineData("symbol=AAPL&strategies=a,b&strategies=a")]
    public void TryParseComparison_Duplicate_IsFieldError(string queryString)
    {
        Errors(queryString).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(BacktestQuery.StrategiesKey, "Strategy 'a' is selected more than once."));
    }

    [Theory] // P4-U16: the settings are read by the same code as TryParse (same fields, same messages)
    [InlineData("symbol=bad*&capital=0&fee=99&from=2024-02-01&to=2024-01-01")]
    [InlineData("symbol=&tf=monthly&slip=abc&sl=0&rf=50")]
    [InlineData("symbol=AAPL&symbol=MSFT&tp=-1&ts=60&to=2024-13-01")]
    public void TryParseComparison_SettingsErrors_AreThoseOfTryParse(string settings)
    {
        BacktestQuery.TryParse(Query(settings + "&strategy=x"), out _, out var backtestErrors).Should().BeFalse();

        Errors(settings + "&strategies=a,b").Should().Equal(backtestErrors);
    }

    [Fact] // P4-X1: strategy, p.*, sims, seed, mc and unknown parameters are ignored and left out of the canonical URL
    public void TryParseComparison_IgnoresStrategyParametersAndMonteCarlo()
    {
        var query = Parse("symbol=AAPL&strategies=a,b&strategy=x&p.fast=abc&p.=1&sims=1&seed=x&mc=no&foo=bar");

        query.ToQueryString().Should().Be("symbol=AAPL&tf=daily&strategies=a,b&capital=10000&fee=0.1&slip=0&rf=0");
    }

    [Fact] // P4-X1: key order of the canonical URL, strategies in the place of strategy, literal commas
    public void ToComparisonQueryString_KeyOrder_AndLiteralCommas()
    {
        var query = Parse("rf=2&ts=3&tp=12.5&sl=5&slip=0.05&fee=0.25&capital=25000.50&to=2024-12-31&from=2020-01-02"
            + "&strategies=sma-crossover,rsi-reversion,buy-and-hold&tf=WEEKLY&symbol=aapl");

        query.ToQueryString().Should().Be(FullCanonical);
    }

    [Theory] // P4-X1: decoded pairs are compared, so an encoded comma or letter never makes a redirect loop
    [InlineData("symbol=AAPL&tf=daily&strategies=fake-threshold%2Ccustom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0")]
    [InlineData("symbol=%41APL&tf=daily&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0")]
    public void IsCanonicalComparison_ComparesDecodedPairs(string received)
    {
        Parse(received).IsCanonical(new QueryString("?" + received)).Should().BeTrue();
    }

    [Theory] // P4-X1: another order, an empty field or an extra key is not canonical
    [InlineData("tf=daily&symbol=AAPL&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0")]
    [InlineData("symbol=AAPL&tf=daily&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&sl=&rf=0")]
    [InlineData("symbol=AAPL&tf=daily&strategies=fake-threshold,custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0&p.lookback=5")]
    [InlineData("symbol=AAPL&tf=daily&strategies=fake-threshold&strategies=custom-trend-dip&capital=10000&fee=0.1&slip=0&rf=0")]
    public void IsCanonicalComparison_OtherForms_AreNot(string received)
    {
        Parse(received).IsCanonical(new QueryString("?" + received)).Should().BeFalse();
    }

    [Fact] // P4-X1, C1: one strategy = the shared settings, this id and every parameter at its default
    public void ForStrategy_HasDefaultParameters_AndTheCanonicalResultUrl()
    {
        var query = Parse(FullCanonical.Replace("sma-crossover,rsi-reversion,buy-and-hold", "fake-threshold,b", StringComparison.Ordinal));

        var single = query.ForStrategy(Threshold);

        single.StrategyId.Should().Be("fake-threshold");
        single.Parameters.Should().BeEquivalentTo(new Dictionary<string, decimal> { ["lookback"] = 10m, ["threshold"] = 0.02m });
        var expected = "symbol=AAPL&tf=weekly&strategy=fake-threshold&from=2020-01-02&to=2024-12-31&capital=25000.5&fee=0.25"
            + "&slip=0.05&sl=5&tp=12.5&ts=3&rf=2&p.lookback=10&p.threshold=0.02";
        single.ToQueryString().Should().Be(expected);
        BacktestQuery.TryParse(Query(expected), out var reparsed, out _).Should().BeTrue();
        reparsed!.WithDefaultParameters(Threshold.Parameters).IsCanonical(new QueryString("?" + expected)).Should().BeTrue();
    }

    [Fact] // C1: same symbol, timeframe, period and execution settings for every strategy
    public void ForStrategy_OnlyTheStrategyAndItsParametersDiffer()
    {
        var query = Parse(FullCanonical);
        var other = new StubStrategy { Id = "buy-and-hold" };

        var a = query.ForStrategy(Threshold).ToBacktestRequest();
        var b = query.ForStrategy(other).ToBacktestRequest();

        b.Should().BeEquivalentTo(a, options => options.Excluding(r => r.StrategyId).Excluding(r => r.Parameters));
        b.Parameters.Should().BeEmpty();
        query.Execution.Should().Be(a.Execution);
    }

    [Theory] // P4-X1: the form keeps its boxes checked from the raw ids
    [InlineData("strategies=a,,b&strategies=c", new[] { "a", "b", "c" })]
    [InlineData("", new string[0])]
    public void ReadStrategyIds_ReadsTheRawIds(string queryString, string[] expected)
    {
        BacktestQuery.ReadStrategyIds(Query(queryString)).Should().Equal(expected);
    }
}
