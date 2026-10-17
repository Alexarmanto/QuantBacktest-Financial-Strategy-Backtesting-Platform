using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using Xunit.Abstractions;

namespace QuantBacktest.Strategies.Tests;

// Tests of the catalog (P2-C1, PART-2 section 7 "Catalogue")
public class StrategyCatalogTests
{
    private readonly ITestOutputHelper _output;

    // Constructor: xUnit gives an object to write text in the test output
    public StrategyCatalogTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GetAll_GivesThe11BuiltInsSortedByName_ThenTheCustomOnes() // P2-C1
    {
        StrategyCatalog catalog = NewCatalog(new FakeCustomStrategyStore());

        IReadOnlyList<IStrategy> all = await catalog.GetAllAsync();

        // the 11 built-in ids, plus the example of the fake store at the end
        all.Count.Should().Be(12);
        List<string> builtInIds = new List<string>();
        for (int i = 0; i < 11; i++)
        {
            builtInIds.Add(all[i].Id);
        }
        builtInIds.Should().BeEquivalentTo(
            "sma-crossover", "bollinger-reversion", "rsi-reversion", "momentum-roc", "macd-crossover", "donchian-breakout",
            "bollinger-rsi-confirmation", "connors-rsi2", "consecutive-down-days", "turn-of-month", "buy-and-hold");
        all[11].Id.Should().Be(FakeCustomStrategyStore.Example.Id);

        // sorted by name
        for (int i = 0; i < 10; i++)
        {
            string.Compare(all[i].Name, all[i + 1].Name, StringComparison.OrdinalIgnoreCase).Should().BeLessThan(0);
        }
    }

    [Fact]
    public async Task GetAll_InvalidSavedDefinition_IsSkipped() // P2-C1
    {
        FakeCustomStrategyStore store = new FakeCustomStrategyStore();
        await store.SaveAsync(InvalidDefinition());
        StrategyCatalog catalog = NewCatalog(store);

        IReadOnlyList<IStrategy> all = await catalog.GetAllAsync();

        all.Count.Should().Be(12);
        foreach (IStrategy strategy in all)
        {
            strategy.Id.Should().NotBe("custom-broken");
        }
    }

    [Fact]
    public async Task Find_ExactIdOnly() // P2-C1
    {
        StrategyCatalog catalog = NewCatalog(new FakeCustomStrategyStore());

        (await catalog.FindAsync("rsi-reversion"))!.Name.Should().Be("RSI mean reversion");
        (await catalog.FindAsync("RSI-REVERSION")).Should().BeNull();
        (await catalog.FindAsync("unknown")).Should().BeNull();
        (await catalog.FindAsync(FakeCustomStrategyStore.Example.Id))!.Name.Should().Be(FakeCustomStrategyStore.Example.Name);
        (await catalog.FindAsync("custom-unknown")).Should().BeNull();
    }

    [Fact]
    public async Task Find_KnownButInvalidCustomStrategy_ThrowsBacktestValidationException() // P2-C1
    {
        FakeCustomStrategyStore store = new FakeCustomStrategyStore();
        await store.SaveAsync(InvalidDefinition());
        StrategyCatalog catalog = NewCatalog(store);

        bool thrown = false;
        try
        {
            await catalog.FindAsync("custom-broken");
        }
        catch (BacktestValidationException)
        {
            thrown = true;
        }

        thrown.Should().BeTrue();
    }

    [Fact]
    public void HiddenStrategy_IsNotInTheCatalog() // P2-C1
    {
        List<IStrategy> strategies = StrategyCatalog.CreateBuiltIns(new Type[] { typeof(SmaCrossoverStrategy), typeof(HiddenTestStrategy) });

        strategies.Count.Should().Be(1);
        strategies[0].Id.Should().Be("sma-crossover");
    }

    [Fact]
    public void ClassesThatCannotBeCreated_AreIgnored() // P2-C1
    {
        // StrategyBase is abstract, NoEmptyConstructorTestStrategy needs a parameter, string is not a strategy
        List<IStrategy> strategies = StrategyCatalog.CreateBuiltIns(
            new Type[] { typeof(StrategyBase), typeof(NoEmptyConstructorTestStrategy), typeof(string), typeof(BuyAndHoldStrategy) });

        strategies.Count.Should().Be(1);
        strategies[0].Id.Should().Be("buy-and-hold");
    }

    [Fact]
    public void TwoStrategiesWithTheSameId_ThrowInvalidOperationException_NamingBothClasses() // P2-C1
    {
        string message = "";
        try
        {
            StrategyCatalog.CreateBuiltIns(new Type[] { typeof(SmaCrossoverStrategy), typeof(DuplicateTestStrategy) });
        }
        catch (InvalidOperationException exception)
        {
            message = exception.Message;
        }

        message.Should().Contain("SmaCrossoverStrategy").And.Contain("DuplicateTestStrategy");
    }

    [Fact]
    public void AddQuantBacktestStrategies_RegistersTheCatalogAsSingleton() // CONTRACT section 2
    {
        ServiceCollection services = new ServiceCollection();

        services.AddQuantBacktestStrategies();

        bool found = false;
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IStrategyCatalog))
            {
                found = true;
                descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
                descriptor.ImplementationType.Should().Be(typeof(StrategyCatalog));
            }
        }
        found.Should().BeTrue();
    }

    [Fact]
    public async Task Catalog_ListsThe11StrategiesWithTheirParameters() // P2-C1, screenshot part2-04-catalog.png
    {
        StrategyCatalog catalog = NewCatalog(new FakeCustomStrategyStore());

        IReadOnlyList<IStrategy> all = await catalog.GetAllAsync();

        int builtIns = 0;
        foreach (IStrategy strategy in all)
        {
            if (strategy.Category == StrategyCategory.Custom)
            {
                continue;
            }
            builtIns++;

            string line = builtIns + ". " + strategy.Id + " | " + strategy.Name + " | " + strategy.Category + " |";
            foreach (ParameterDefinition parameter in strategy.Parameters)
            {
                line = line + " " + parameter.Key + "=" + parameter.DefaultValue + " [" + parameter.Min + ", " + parameter.Max + "]";
            }
            _output.WriteLine(line);
        }

        builtIns.Should().Be(11);
    }

    // ---- helpers ----

    private static StrategyCatalog NewCatalog(ICustomStrategyStore store)
    {
        return new StrategyCatalog(store, new FakeCustomStrategyFactory(), NullLogger<StrategyCatalog>.Instance);
    }

    // the id should be "custom-broken-strategy" for this name, so the factory refuses it
    private static CustomStrategyDefinition InvalidDefinition()
    {
        RuleSet rules = FakeCustomStrategyStore.Example.Entry;
        return new CustomStrategyDefinition("custom-broken", "Broken strategy", "", rules, rules);
    }
}

// ---- small strategy classes used only by the catalog tests ----

[HiddenStrategy]
internal class HiddenTestStrategy : SmaCrossoverStrategy
{
}

internal class DuplicateTestStrategy : SmaCrossoverStrategy
{
}

internal class NoEmptyConstructorTestStrategy : BuyAndHoldStrategy
{
    public NoEmptyConstructorTestStrategy(int unused)
    {
        Unused = unused;
    }

    public int Unused { get; private set; }
}
