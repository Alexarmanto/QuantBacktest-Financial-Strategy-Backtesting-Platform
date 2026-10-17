using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Strategies.Tests;

// How a no-code strategy behaves once it is created (R5 to R8, P2-N2, P2-N3)
public class RuleBasedStrategyTests
{
    private const Signal H = Signal.Hold;
    private const Signal B = Signal.Buy;
    private const Signal S = Signal.Sell;
    private static readonly Dictionary<string, double> NoParameters = new Dictionary<string, double>();

    // closes 1, 2, 3, 2, 1 compared with the constant 2
    private static readonly List<PriceBar> UpAndDown = TestBars.FromCloses(new double[] { 1, 2, 3, 2, 1 });

    [Theory]
    [InlineData(ComparisonOperator.GreaterThan, new[] { H, H, B, H, H })]   // 3 > 2 only
    [InlineData(ComparisonOperator.LessThan, new[] { B, H, H, H, B })]      // 1 < 2 twice
    [InlineData(ComparisonOperator.CrossesAbove, new[] { H, H, B, H, H })]  // from 2 (equal) to 3
    [InlineData(ComparisonOperator.CrossesBelow, new[] { H, H, H, H, B })]  // from 2 (equal) to 1
    public void EachOperator_GivesTheExpectedBars(ComparisonOperator comparison, Signal[] expected) // R6
    {
        // exit rule never true (Close < 0), so the signals show where the entry rule is true
        IStrategy strategy = Create(
            All(new RuleCondition(Close(), comparison, Constant(2))),
            All(new RuleCondition(Close(), ComparisonOperator.LessThan, Constant(0))));

        strategy.GenerateSignals(UpAndDown, NoParameters).Should().Equal(expected);
    }

    [Fact]
    public void All_NeedsEveryCondition_Any_NeedsOnlyOne() // R2
    {
        RuleCondition aboveOne = new RuleCondition(Close(), ComparisonOperator.GreaterThan, Constant(1));
        RuleCondition belowThree = new RuleCondition(Close(), ComparisonOperator.LessThan, Constant(3));
        RuleCondition belowTwo = new RuleCondition(Close(), ComparisonOperator.LessThan, Constant(2));
        RuleCondition aboveTwo = new RuleCondition(Close(), ComparisonOperator.GreaterThan, Constant(2));
        RuleSet never = All(new RuleCondition(Close(), ComparisonOperator.LessThan, Constant(0)));

        IStrategy allStrategy = Create(All(aboveOne, belowThree), never);
        IStrategy anyStrategy = Create(new RuleSet(RuleCombinator.Any, new RuleCondition[] { belowTwo, aboveTwo }), never);

        allStrategy.GenerateSignals(UpAndDown, NoParameters).Should().Equal(H, B, H, B, H);
        anyStrategy.GenerateSignals(UpAndDown, NoParameters).Should().Equal(B, H, B, H, B);
    }

    [Fact]
    public void EntryAndExitBothTrue_GivesSell() // R7, S4
    {
        RuleSet always = All(new RuleCondition(Close(), ComparisonOperator.GreaterThan, Constant(0)));

        IStrategy strategy = Create(always, always);

        strategy.GenerateSignals(UpAndDown, NoParameters).Should().Equal(S, S, S, S, S);
    }

    [Fact]
    public void OperandInWarmUp_MakesTheConditionFalse() // R6
    {
        // SMA(3) is not defined on the first 2 bars, so "SMA(3) < 100" is false there
        IStrategy strategy = Create(
            All(new RuleCondition(new Operand(OperandKind.Sma, Period: 3), ComparisonOperator.LessThan, Constant(100))),
            All(new RuleCondition(Close(), ComparisonOperator.LessThan, Constant(0))));

        strategy.GenerateSignals(UpAndDown, NoParameters).Should().Equal(H, H, B, B, B);
    }

    [Fact]
    public void Documentation_IsWrittenFromTheRules() // R8, P2-N2
    {
        IStrategy strategy = new RuleStrategyFactory().Create(FakeCustomStrategyStore.Example);

        strategy.Documentation.Idea.Should().Be(FakeCustomStrategyStore.Example.Description);
        strategy.Documentation.EntryRule.Should().Be("Buy when ALL of: Close > SMA(200); RSI(2) < 10");
        strategy.Documentation.ExitRule.Should().Be("Sell when ANY of: Close crosses above SMA(5)");
        strategy.Documentation.Risks.Should().Be("User-defined rules. Check the train/test page before trusting the results.");
    }

    [Fact]
    public void Documentation_EmptyDescription_GivesTheDefaultIdea() // P2-N2
    {
        IStrategy strategy = Create(
            All(new RuleCondition(new Operand(OperandKind.BollingerLower, Period: 20, StdDevs: 2.5), ComparisonOperator.CrossesBelow, Close())),
            All(new RuleCondition(new Operand(OperandKind.MacdLine), ComparisonOperator.CrossesAbove, new Operand(OperandKind.MacdSignal))));

        strategy.Documentation.Idea.Should().Be("Custom no-code strategy.");
        strategy.Documentation.EntryRule.Should().Be("Buy when ALL of: BB lower(20, 2.5) crosses below Close");
        strategy.Documentation.ExitRule.Should().Be("Sell when ALL of: MACD line(12, 26, 9) crosses above MACD signal(12, 26, 9)");
    }

    [Fact]
    public void Indicators_OneLinePerDifferentOperand_WithoutPricesAndConstants() // R8, P2-N3
    {
        IStrategy strategy = new RuleStrategyFactory().Create(FakeCustomStrategyStore.Example);
        IReadOnlyList<PriceBar> bars = SyntheticPrices.Generate("TEST", Timeframe.Daily, 300, 42).Bars;
        double[] closes = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            closes[i] = (double)bars[i].Close;
        }

        IReadOnlyList<IndicatorSeries> series = strategy.ComputeIndicators(bars, NoParameters);

        // Close and the constant 10 are not drawn: SMA(200), RSI(2), SMA(5)
        series.Count.Should().Be(3);
        series[0].Key.Should().Be("sma-200");
        series[0].Label.Should().Be("SMA(200)");
        series[0].Pane.Should().Be(IndicatorPane.Price);
        series[1].Key.Should().Be("rsi-2");
        series[1].Pane.Should().Be(IndicatorPane.Oscillator);
        series[2].Key.Should().Be("sma-5");
        series[0].Values.Should().Equal(Indicators.ToNullable(Indicators.Sma(closes, 200)));
        series[1].Values.Count.Should().Be(300);
    }

    [Fact]
    public void Indicators_SameBollingerBandTwice_IsDrawnOnce() // P2-N3
    {
        // no StdDevs means 2 (R3), so both operands are "BB lower(20, 2)"
        IStrategy strategy = Create(
            All(new RuleCondition(Close(), ComparisonOperator.LessThan, new Operand(OperandKind.BollingerLower, Period: 20))),
            All(new RuleCondition(Close(), ComparisonOperator.GreaterThan, new Operand(OperandKind.BollingerLower, Period: 20, StdDevs: 2))));

        IReadOnlyList<IndicatorSeries> series = strategy.ComputeIndicators(UpAndDown, NoParameters);

        series.Count.Should().Be(1);
        series[0].Key.Should().Be("bb-lower-20-2");
        series[0].Label.Should().Be("BB lower(20, 2)");
    }

    [Fact]
    public void Example_BuysExactlyWhereItsRulesAreTrue_BarByBar() // R5, R6, R7 with Close > SMA(200), RSI(2) < 10
    {
        IStrategy strategy = new RuleStrategyFactory().Create(FakeCustomStrategyStore.Example);
        IReadOnlyList<PriceBar> bars = SyntheticPrices.Generate("TEST", Timeframe.Daily, 1000, 42).Bars;
        double[] closes = new double[bars.Count];
        for (int i = 0; i < bars.Count; i++)
        {
            closes[i] = (double)bars[i].Close;
        }
        double[] sma200 = Indicators.Sma(closes, 200);
        double[] rsi2 = Indicators.Rsi(closes, 2);
        double[] sma5 = Indicators.Sma(closes, 5);

        IReadOnlyList<Signal> signals = strategy.GenerateSignals(bars, NoParameters);

        int buys = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            bool entry = closes[i] > sma200[i] && rsi2[i] < 10;
            bool exit = Indicators.CrossesAbove(closes, sma5, i);

            Signal expected = H;
            if (exit)
            {
                expected = S;
            }
            else if (entry)
            {
                expected = B;
                buys++;
            }
            signals[i].Should().Be(expected, $"bar {i}");
        }

        // the example must really buy somewhere, otherwise the test would prove nothing
        buys.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Catalog_WithTheRealFactory_ShowsTheSavedStrategy() // P2-C1, P2-N1
    {
        StrategyCatalog catalog = new StrategyCatalog(new FakeCustomStrategyStore(), new RuleStrategyFactory(), NullLogger<StrategyCatalog>.Instance);

        IReadOnlyList<IStrategy> all = await catalog.GetAllAsync();

        all.Count.Should().Be(12);
        all[11].Documentation.EntryRule.Should().Be("Buy when ALL of: Close > SMA(200); RSI(2) < 10");
    }

    [Fact]
    public void AddQuantBacktestStrategies_RegistersTheFactoryAsSingleton() // CONTRACT section 2
    {
        ServiceCollection services = new ServiceCollection();

        services.AddQuantBacktestStrategies();

        bool found = false;
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ICustomStrategyFactory))
            {
                found = true;
                descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
                descriptor.ImplementationType.Should().Be(typeof(RuleStrategyFactory));
            }
        }
        found.Should().BeTrue();
    }

    // ---- helpers ----

    private static IStrategy Create(RuleSet entry, RuleSet exit)
    {
        return new RuleStrategyFactory().Create(new CustomStrategyDefinition("custom-test", "Test", "", entry, exit));
    }

    private static RuleSet All(params RuleCondition[] conditions)
    {
        return new RuleSet(RuleCombinator.All, conditions);
    }

    private static Operand Close()
    {
        return new Operand(OperandKind.Close);
    }

    private static Operand Constant(double value)
    {
        return new Operand(OperandKind.Constant, Value: value);
    }
}
