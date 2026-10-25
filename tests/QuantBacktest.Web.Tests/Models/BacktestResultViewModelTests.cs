using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class BacktestResultViewModelTests
{
    private static readonly StubStrategy Crossover = new()
    {
        Id = "sma-crossover",
        Parameters =
        [
            new ParameterDefinition("fast", "Fast period", ParameterType.Integer, 20, 2, 200, 1),
            new ParameterDefinition("slow", "Slow period", ParameterType.Integer, 50, 5, 400, 1),
        ],
    };

    private static PerformanceMetrics Metrics(double? sharpe) =>
        new(10371.39m, 0.0371, 0.0185, 0.0002, 0.15, sharpe, null, null, 0.1234, 4, 0.5, null, 0.6);

    private static BacktestResult Result(IReadOnlyDictionary<string, double> parameters, ExecutionSettings execution) =>
        new(
            new BacktestRequest("AAPL", Timeframe.Weekly, "sma-crossover", parameters, null, null, execution),
            "Moving average crossover",
            PricesAdjusted: true,
            [
                new PriceBar(new DateOnly(2024, 1, 5), 1, 1, 1, 1, 0),
                new PriceBar(new DateOnly(2024, 6, 28), 1, 1, 1, 1, 0),
            ],
            [],
            [],
            Metrics(sharpe: 1.234),
            Metrics(sharpe: null),
            []);

    [Fact] // P4-U14: metrics formatted with DisplayFormat; null → "n/a" (CONTRACT section 3)
    public void Metrics_AreFormatted()
    {
        var model = BacktestResultViewModel.Create(Result(new Dictionary<string, double>(), ExecutionSettings.Default), Crossover, "http://x");

        model.Metrics.Should().Equal(
            new MetricRow("Final equity", "$10,371.39", "$10,371.39"),
            new MetricRow("Cumulative return", "3.71%", "3.71%"),
            new MetricRow("Annualized return", "1.85%", "1.85%"),
            new MetricRow("Sharpe ratio", "1.23", "n/a"),
            new MetricRow("Max drawdown", "12.34%", "12.34%"),
            new MetricRow("Trades", "4", "4"),
            new MetricRow("Win rate", "50.00%", "50.00%"));
        model.Period.Should().Be("2024-01-05 to 2024-06-28");
        model.Timeframe.Should().Be("Weekly");
    }

    [Fact] // P4-U3 (settings): fractions shown as percentages, disabled exits shown as "Off"
    public void Execution_IsShownInPercent()
    {
        var execution = new ExecutionSettings(25000m, 0.0025m, 0m, StopLossFraction: 0.05m, RiskFreeRate: 0.02);

        var model = BacktestResultViewModel.Create(Result(new Dictionary<string, double>(), execution), Crossover, "http://x");

        model.Execution.Should().Equal(
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
        var model = BacktestResultViewModel.Create(
            Result(new Dictionary<string, double> { ["fast"] = 10 }, ExecutionSettings.Default), Crossover, "http://x");

        model.Parameters.Should().Equal(new SettingRow("Fast period", "10"), new SettingRow("Slow period", "50"));
    }
}
