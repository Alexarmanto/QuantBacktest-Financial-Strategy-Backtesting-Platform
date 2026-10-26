using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class BacktestResultViewModelTests
{
    private const string Canonical =
        "symbol=AAPL&tf=weekly&strategy=sma-crossover&capital=10000&fee=0.1&slip=0&rf=0&p.fast=20&p.slow=50";

    private static readonly StubStrategy Crossover = new()
    {
        Id = "sma-crossover",
        Documentation = new StrategyDocumentation("The idea.", "The entry.", "The exit.", "The risks."),
        Parameters =
        [
            new ParameterDefinition("fast", "Fast period", ParameterType.Integer, 20, 2, 200, 1),
            new ParameterDefinition("slow", "Slow period", ParameterType.Integer, 50, 5, 400, 1),
        ],
    };

    /// <summary>Every field has a distinct value, so a swapped field shows up.</summary>
    private static readonly PerformanceMetrics StrategyMetrics =
        new(10371.39m, 0.0371, 0.0185, 0.0002, 0.15, 1.234, 2.346, 3.456, 0.1234, 4, 0.5, 1.75, 0.6);

    /// <summary>Undefined ratios (M6, M7, M9, M11) and negative returns.</summary>
    private static readonly PerformanceMetrics BenchmarkMetrics =
        new(9500m, -0.05, -0.0255, -0.0001, 0.2, null, null, null, 0.3, 1, 0, null, 1);

    private static BacktestQuery Query(string queryString = Canonical)
    {
        BacktestQuery.TryParse(new QueryCollection(QueryHelpers.ParseQuery(queryString)), out var query, out _).Should().BeTrue();
        return query!;
    }

    private static BacktestResult Result(
        IReadOnlyDictionary<string, double>? parameters = null,
        ExecutionSettings? execution = null,
        Timeframe timeframe = Timeframe.Weekly,
        bool pricesAdjusted = true,
        PerformanceMetrics? metrics = null,
        IReadOnlyList<Trade>? trades = null) =>
        new(
            new BacktestRequest(
                "AAPL", timeframe, "sma-crossover", parameters ?? new Dictionary<string, double>(), null, null, execution ?? ExecutionSettings.Default),
            "Moving average crossover",
            pricesAdjusted,
            [
                new PriceBar(new DateOnly(2024, 1, 5), 1, 1, 1, 1, 0),
                new PriceBar(new DateOnly(2024, 6, 28), 1, 1, 1, 1, 0),
            ],
            [],
            trades ?? [],
            metrics ?? StrategyMetrics,
            BenchmarkMetrics,
            []);

    private static BacktestResultViewModel Create(BacktestResult result, StubStrategy? strategy = null) =>
        BacktestResultViewModel.Create(result, strategy ?? Crossover, Query(), "http://x", pathBase: "");

    // ---- Metrics (P4-U4) ----

    [Fact] // P4-U4, M1 to M13: the 13 fields in the order of the record, formatted; null → "n/a"
    public void Metrics_AreTheThirteenFields_InTheOrderOfTheRecord()
    {
        var model = Create(Result());

        model.Metrics.Select(m => (m.Label, m.Strategy, m.BuyAndHold)).Should().Equal(
            ("Final equity", "$10,371.39", "$9,500.00"),
            ("Cumulative return", "3.71%", "-5.00%"),
            ("Annualized return", "1.85%", "-2.55%"),
            ("Average weekly return", "0.02%", "-0.01%"),
            ("Annualized volatility", "15.00%", "20.00%"),
            ("Sharpe ratio", "1.23", "n/a"),
            ("Sortino ratio", "2.35", "n/a"),
            ("Calmar ratio", "3.46", "n/a"),
            ("Max drawdown", "12.34%", "30.00%"),
            ("Trades", "4", "1"),
            ("Win rate", "50.00%", "0.00%"),
            ("Profit factor", "1.75", "n/a"),
            ("Exposure", "60.00%", "100.00%"));
    }

    [Theory] // M4: AverageReturnPerPeriod is labelled after the timeframe
    [InlineData(Timeframe.Daily, "Average daily return")]
    [InlineData(Timeframe.Weekly, "Average weekly return")]
    public void AverageReturn_IsLabelledAfterTheTimeframe(Timeframe timeframe, string label)
    {
        Create(Result(timeframe: timeframe)).Metrics[3].Label.Should().Be(label);
    }

    [Fact] // P4-U4: green / red on the three returns only, from the sign of the value
    public void Returns_AreColouredBySign()
    {
        var model = Create(Result());

        foreach (var row in model.Metrics.Skip(1).Take(3))
        {
            row.StrategyTone.Should().Be(ValueTone.Gain, row.Label);
            row.BuyAndHoldTone.Should().Be(ValueTone.Loss, row.Label);
        }
    }

    [Fact] // P4-U4: a zero return is neutral
    public void ZeroReturns_AreNeutral()
    {
        var flat = StrategyMetrics with { CumulativeReturn = 0, AnnualizedReturn = 0, AverageReturnPerPeriod = 0 };

        var model = Create(Result(metrics: flat));

        model.Metrics.Skip(1).Take(3).Select(r => r.StrategyTone).Should().AllBe(ValueTone.Neutral);
    }

    [Fact] // P4-U4: no colour outside the returns (drawdown and volatility are positive but not gains)
    public void OtherMetrics_AreNotColoured()
    {
        var model = Create(Result());

        var others = model.Metrics.Where((_, index) => index is 0 or > 3).ToList();
        others.Should().HaveCount(10);
        others.Should().OnlyContain(r => r.StrategyTone == ValueTone.Neutral && r.BuyAndHoldTone == ValueTone.Neutral);
    }

    // ---- Header (P4-U3) ----

    [Fact] // P4-U3: actual period from Bars, timeframe, strategy card
    public void Header_ShowsWhatWasTested()
    {
        var header = Create(Result()).Header;

        header.Symbol.Should().Be("AAPL");
        header.StrategyName.Should().Be("Moving average crossover");
        header.Timeframe.Should().Be("Weekly");
        header.Period.Should().Be("2024-01-05 to 2024-06-28");
        header.Permalink.Should().Be("http://x");
        header.Documentation.Should().Be(Crossover.Documentation);
    }

    [Fact] // P4-U3: warning with the exact text when the prices are not adjusted
    public void Header_UnadjustedPrices_ShowsTheWarning()
    {
        Create(Result(pricesAdjusted: false)).Header.UnadjustedWarning
            .Should().Be("Prices are not adjusted for splits or dividends: long periods may be distorted.");
    }

    [Fact] // P4-U3: no warning when the prices are adjusted
    public void Header_AdjustedPrices_HasNoWarning()
    {
        Create(Result(pricesAdjusted: true)).Header.UnadjustedWarning.Should().BeNull();
    }

    [Fact] // P4-U3 (settings): fractions shown as percentages, disabled exits shown as "Off"
    public void Execution_IsShownInPercent()
    {
        var execution = new ExecutionSettings(25000m, 0.0025m, 0m, StopLossFraction: 0.05m, RiskFreeRate: 0.02);

        Create(Result(execution: execution)).Header.Execution.Should().Equal(
            new SettingRow("Initial capital", "$25,000.00"),
            new SettingRow("Fee", "0.25%"),
            new SettingRow("Slippage", "0.00%"),
            new SettingRow("Stop-loss", "5.00%"),
            new SettingRow("Take-profit", "Off"),
            new SettingRow("Trailing stop", "Off"),
            new SettingRow("Risk-free rate", "2.00%"));
    }

    [Fact] // CONTRACT section 3: a key absent from the request runs with its default, shown as such
    public void Parameters_AreCompletedWithTheCatalogDefaults()
    {
        var model = Create(Result(parameters: new Dictionary<string, double> { ["fast"] = 10 }));

        model.Header.Parameters.Should().Equal(new SettingRow("Fast period", "10"), new SettingRow("Slow period", "50"));
    }

    // ---- Trades and actions are assembled (details: ResultTradesViewModelTests, ResultActionsViewModelTests) ----

    [Fact] // P4-U6: one row per trade, in the order of the result
    public void Trades_FollowTheResult()
    {
        var trades = new[]
        {
            new Trade(new DateOnly(2024, 2, 2), 10m, new DateOnly(2024, 3, 1), 11m, 100m, 2m, 98m, 0.098, ExitReason.Signal, 4),
            new Trade(new DateOnly(2024, 4, 5), 12m, new DateOnly(2024, 5, 3), 11m, 90m, 2m, -92m, -0.085, ExitReason.StopLoss, 4),
        };

        var rows = Create(Result(trades: trades)).Trades.Rows;

        rows.Select(r => r.EntryDate.Text).Should().Equal("2024-02-02", "2024-04-05");
    }

    [Fact] // P4-U7: the actions are built from the effective query
    public void Actions_UseTheQuery()
    {
        Create(Result()).Actions.EditSettingsUrl.Should().Be("/Backtest?" + Canonical);
    }
}
