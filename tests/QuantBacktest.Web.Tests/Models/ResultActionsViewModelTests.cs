using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class ResultActionsViewModelTests
{
    private const string Canonical =
        "symbol=AAPL&tf=daily&strategy=sma-crossover&from=2020-01-02&capital=10000&fee=0.1&slip=0&sl=5&rf=0&p.fast=20&p.slow=50";

    private const int EnoughTrades = 12;

    private static BacktestQuery Query()
    {
        BacktestQuery.TryParse(new QueryCollection(QueryHelpers.ParseQuery(Canonical)), out var query, out _).Should().BeTrue();
        return query!;
    }

    private static StubStrategy WithParameters(int count) => new()
    {
        Id = "sma-crossover",
        Parameters = Enumerable.Range(0, count)
            .Select(i => new ParameterDefinition($"k{i}", $"K{i}", ParameterType.Integer, 1, 0, 10, 1))
            .ToList(),
    };

    [Fact] // P4-U7, PART-4 section 5: every action carries the canonical query string of the result
    public void Urls_CarryTheCanonicalQuery()
    {
        var actions = ResultActionsViewModel.Create(Query(), WithParameters(2), EnoughTrades, pathBase: "");

        actions.EditSettingsUrl.Should().Be("/Backtest?" + Canonical);
        actions.PdfUrl.Should().Be("/Backtest/Report.pdf?" + Canonical);
        actions.TradesCsvUrl.Should().Be("/Backtest/Trades.csv?" + Canonical);
        actions.OptimizeUrl.Should().Be("/Optimize?" + Canonical);
    }

    [Fact] // P4-X4: "Run Monte Carlo" goes straight to the canonical URL, defaults of Conventions included (no redirect)
    public void MonteCarloUrl_IsCanonical_WithTheDefaultSimulationsAndSeed()
    {
        var actions = ResultActionsViewModel.Create(Query(), WithParameters(2), EnoughTrades, pathBase: "");

        actions.MonteCarloUrl.Should().Be("/Backtest/MonteCarlo?" + Canonical + "&sims=1000&seed=42");
    }

    [Theory] // MC1: the button needs Conventions.MinTradesForMonteCarlo trades (Trades.Count, the k of MC1)
    [InlineData(0, false)]
    [InlineData(Conventions.MinTradesForMonteCarlo - 1, false)]
    [InlineData(Conventions.MinTradesForMonteCarlo, true)]
    [InlineData(40, true)]
    public void MonteCarlo_NeedsFiveTrades(int tradeCount, bool enabled)
    {
        var actions = ResultActionsViewModel.Create(Query(), WithParameters(0), tradeCount, pathBase: "");

        (actions.MonteCarloUrl is not null).Should().Be(enabled);
        ResultActionsViewModel.MonteCarloNeedsTrades.Should().Be("Needs at least 5 trades");
    }

    [Fact] // decision E: the path base of the request prefixes every URL
    public void Urls_KeepThePathBase()
    {
        var actions = ResultActionsViewModel.Create(Query(), WithParameters(2), EnoughTrades, pathBase: "/app");

        actions.EditSettingsUrl.Should().Be("/app/Backtest?" + Canonical);
        actions.OptimizeUrl.Should().Be("/app/Optimize?" + Canonical);
        actions.MonteCarloUrl.Should().StartWith("/app/Backtest/MonteCarlo?");
    }

    [Theory] // P4-U7: "Optimize this strategy" needs at least 2 parameters (heatmap X and Y)
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void Optimize_NeedsTwoParameters(int parameterCount, bool shown)
    {
        var actions = ResultActionsViewModel.Create(Query(), WithParameters(parameterCount), EnoughTrades, pathBase: "");

        (actions.OptimizeUrl is not null).Should().Be(shown);
    }
}
