using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Strategies.Tests;

// The C# template (P2-C3): hidden from the catalog, but it works, and a copy without [HiddenStrategy] is shown
public class TemplateStrategyTests
{
    [Fact]
    public async Task Template_IsNotInTheCatalog() // P2-C3, P2-C1
    {
        StrategyCatalog catalog = new StrategyCatalog(new FakeCustomStrategyStore(), new RuleStrategyFactory(), NullLogger<StrategyCatalog>.Instance);

        (await catalog.FindAsync("template-strategy")).Should().BeNull();
        (await catalog.GetAllAsync()).Count.Should().Be(12);
    }

    [Fact]
    public void Template_GivesSignalsLikeAnyStrategy() // P2-C3
    {
        // SMA(2): -, 4.5, 3.5, 2.5, 2.5, 3.5, 4.5, 4
        List<PriceBar> bars = TestBars.FromCloses(new double[] { 5, 4, 3, 2, 3, 4, 5, 3 });
        Dictionary<string, double> parameters = new Dictionary<string, double> { { "period", 2 } };

        IReadOnlyList<Signal> signals = new TemplateStrategy().GenerateSignals(bars, parameters);

        // bar 4: close 3 > SMA 2.5 after 2 <= 2.5 -> Buy ; bar 7: close 3 < SMA 4 after 5 >= 4.5 -> Sell
        signals.Should().Equal(Signal.Hold, Signal.Hold, Signal.Hold, Signal.Hold, Signal.Buy, Signal.Hold, Signal.Hold, Signal.Sell);
        new TemplateStrategy().ComputeIndicators(bars, parameters).Count.Should().Be(1);
    }

    [Fact]
    public void CopyWithoutHiddenStrategy_IsFoundByTheCatalog() // P2-C3, Definition of Done
    {
        // CopiedFromTemplate (below) is what a user gets after copying the file and removing [HiddenStrategy]
        List<IStrategy> strategies = StrategyCatalog.CreateBuiltIns(new Type[] { typeof(TemplateStrategy), typeof(CopiedFromTemplate) });

        strategies.Count.Should().Be(1);
        strategies[0].Id.Should().Be("template-strategy");
    }
}

// a copy of the template without the [HiddenStrategy] line
internal class CopiedFromTemplate : TemplateStrategy
{
}
