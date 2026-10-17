using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Strategies.Tests;

// Tests run on each of the 11 strategies (PART-2 section 7, "Chaque strategie")
public class StrategyContractTests
{
    private static readonly Dictionary<string, double> Defaults = new Dictionary<string, double>();

    [Theory]
    [MemberData(nameof(TestStrategies.Ids), MemberType = typeof(TestStrategies))]
    public void NameCategoryTimeframesAndParameters_MatchTheContractTable(string id) // CONTRACT section 8, P2-S13
    {
        IStrategy strategy = TestStrategies.Find(id);
        ExpectedStrategy expected = Expected(id);

        strategy.Name.Should().Be(expected.Name);
        strategy.Category.Should().Be(expected.Category);
        strategy.SupportedTimeframes.Should().Equal(expected.Timeframes);
        strategy.Parameters.Count.Should().Be(expected.Parameters.Length);
        for (int i = 0; i < expected.Parameters.Length; i++)
        {
            ParameterDefinition actual = strategy.Parameters[i];
            ParameterDefinition wanted = expected.Parameters[i];
            actual.Key.Should().Be(wanted.Key);
            actual.Type.Should().Be(wanted.Type);
            actual.DefaultValue.Should().Be(wanted.DefaultValue);
            actual.Min.Should().Be(wanted.Min);
            actual.Max.Should().Be(wanted.Max);
            actual.Step.Should().Be(wanted.Step);
        }
    }

    [Theory]
    [MemberData(nameof(TestStrategies.Ids), MemberType = typeof(TestStrategies))]
    public void ParametersAndTexts_AreConsistent(string id) // P2-S12
    {
        IStrategy strategy = TestStrategies.Find(id);

        foreach (ParameterDefinition parameter in strategy.Parameters)
        {
            parameter.Min.Should().BeLessThanOrEqualTo(parameter.DefaultValue);
            parameter.DefaultValue.Should().BeLessThanOrEqualTo(parameter.Max);
            parameter.Step.Should().BeGreaterThan(0);
            parameter.Label.Should().NotBeNullOrWhiteSpace();
        }
        strategy.Description.Should().NotBeNullOrWhiteSpace();
        strategy.Documentation.Idea.Should().NotBeNullOrWhiteSpace();
        strategy.Documentation.EntryRule.Should().NotBeNullOrWhiteSpace();
        strategy.Documentation.ExitRule.Should().NotBeNullOrWhiteSpace();
        strategy.Documentation.Risks.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(TestStrategies.Ids), MemberType = typeof(TestStrategies))]
    public void DefaultParameters_OneSignalPerBar_On500And5And1Bars(string id) // S2
    {
        IStrategy strategy = TestStrategies.Find(id);
        IReadOnlyList<PriceBar> bars500 = SyntheticPrices.Generate("TEST", Timeframe.Daily, 500, 42).Bars;
        IReadOnlyList<PriceBar> bars5 = SyntheticPrices.Generate("TEST", Timeframe.Daily, 5, 42).Bars;
        List<PriceBar> bars1 = new List<PriceBar> { bars5[0] };

        strategy.GenerateSignals(bars500, Defaults).Count.Should().Be(500);
        strategy.GenerateSignals(bars5, Defaults).Count.Should().Be(5);
        strategy.GenerateSignals(bars1, Defaults).Count.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(TestStrategies.Ids), MemberType = typeof(TestStrategies))]
    public void EachIndicatorSeries_HasOneValuePerBar(string id) // S6
    {
        IStrategy strategy = TestStrategies.Find(id);
        IReadOnlyList<PriceBar> bars = SyntheticPrices.Generate("TEST", Timeframe.Daily, 500, 42).Bars;

        foreach (IndicatorSeries series in strategy.ComputeIndicators(bars, Defaults))
        {
            series.Values.Count.Should().Be(500);
        }
        foreach (IndicatorSeries series in strategy.ComputeIndicators(new List<PriceBar> { bars[0] }, Defaults))
        {
            series.Values.Count.Should().Be(1);
        }
    }

    [Theory]
    [MemberData(nameof(TestStrategies.Ids), MemberType = typeof(TestStrategies))]
    public void DuringTheWarmUp_EverySignalIsHold(string id) // S2
    {
        IStrategy strategy = TestStrategies.Find(id);
        IReadOnlyList<PriceBar> bars = SyntheticPrices.Generate("TEST", Timeframe.Daily, 500, 42).Bars;

        IReadOnlyList<Signal> signals = strategy.GenerateSignals(bars, Defaults);

        for (int i = 0; i < TestStrategies.WarmUpBars(id); i++)
        {
            signals[i].Should().Be(Signal.Hold, $"bar {i} is in the warm-up");
        }
    }

    // ---- the table of CONTRACT.md section 8 ----

    private static ExpectedStrategy Expected(string id)
    {
        Timeframe[] dailyAndWeekly = new Timeframe[] { Timeframe.Daily, Timeframe.Weekly };
        ParameterType integer = ParameterType.Integer;
        ParameterType number = ParameterType.Decimal;

        switch (id)
        {
            case "sma-crossover":
                return new ExpectedStrategy("Moving average crossover", StrategyCategory.TrendFollowing, dailyAndWeekly,
                    P("fast", integer, 20, 2, 200, 1), P("slow", integer, 50, 5, 400, 1));
            case "bollinger-reversion":
                return new ExpectedStrategy("Bollinger mean reversion", StrategyCategory.MeanReversion, dailyAndWeekly,
                    P("period", integer, 20, 5, 100, 1), P("stdDevs", number, 2, 0.5, 4, 0.1), P("exitLevel", integer, 1, 0, 1, 1));
            case "rsi-reversion":
                return new ExpectedStrategy("RSI mean reversion", StrategyCategory.MeanReversion, dailyAndWeekly,
                    P("period", integer, 14, 2, 50, 1), P("oversold", number, 30, 5, 50, 1), P("overbought", number, 70, 50, 95, 1));
            case "momentum-roc":
                return new ExpectedStrategy("Momentum (rate of change)", StrategyCategory.Momentum, dailyAndWeekly,
                    P("lookback", integer, 14, 2, 250, 1), P("threshold", number, 0, 0, 0.5, 0.005));
            case "macd-crossover":
                return new ExpectedStrategy("MACD crossover", StrategyCategory.TrendFollowing, dailyAndWeekly,
                    P("fast", integer, 12, 2, 100, 1), P("slow", integer, 26, 5, 200, 1), P("signal", integer, 9, 2, 50, 1));
            case "donchian-breakout":
                return new ExpectedStrategy("Donchian channel breakout", StrategyCategory.Breakout, dailyAndWeekly,
                    P("entryLookback", integer, 20, 5, 250, 1), P("exitLookback", integer, 10, 3, 250, 1), P("threshold", number, 0, 0, 0.1, 0.001));
            case "bollinger-rsi-confirmation":
                return new ExpectedStrategy("Bollinger + RSI double confirmation", StrategyCategory.MeanReversion, dailyAndWeekly,
                    P("period", integer, 20, 5, 100, 1), P("stdDevs", number, 2, 0.5, 4, 0.1), P("rsiPeriod", integer, 14, 2, 50, 1),
                    P("oversold", number, 30, 5, 50, 1), P("overbought", number, 70, 50, 95, 1));
            case "connors-rsi2":
                return new ExpectedStrategy("Trend-filtered RSI-2 (Connors)", StrategyCategory.MeanReversion, dailyAndWeekly,
                    P("trendPeriod", integer, 200, 50, 300, 10), P("rsiPeriod", integer, 2, 2, 10, 1),
                    P("entryThreshold", number, 10, 1, 40, 1), P("exitPeriod", integer, 5, 2, 20, 1));
            case "consecutive-down-days":
                return new ExpectedStrategy("Consecutive down days", StrategyCategory.MeanReversion, dailyAndWeekly,
                    P("downDays", integer, 3, 2, 7, 1), P("minDrop", number, 0, 0, 0.1, 0.005));
            case "turn-of-month":
                return new ExpectedStrategy("Turn-of-the-month effect", StrategyCategory.Calendar, new Timeframe[] { Timeframe.Daily },
                    P("daysBeforeEnd", integer, 1, 1, 5, 1), P("daysAfterStart", integer, 3, 1, 10, 1));
            case "buy-and-hold":
                return new ExpectedStrategy("Buy and hold (baseline)", StrategyCategory.Baseline, dailyAndWeekly);
            default:
                throw new ArgumentException("Unknown strategy " + id);
        }
    }

    private static ParameterDefinition P(string key, ParameterType type, double defaultValue, double min, double max, double step)
    {
        return new ParameterDefinition(key, key, type, defaultValue, min, max, step);
    }

    private class ExpectedStrategy
    {
        public string Name { get; private set; }
        public StrategyCategory Category { get; private set; }
        public Timeframe[] Timeframes { get; private set; }
        public ParameterDefinition[] Parameters { get; private set; }

        public ExpectedStrategy(string name, StrategyCategory category, Timeframe[] timeframes, params ParameterDefinition[] parameters)
        {
            Name = name;
            Category = category;
            Timeframes = timeframes;
            Parameters = parameters;
        }
    }
}
