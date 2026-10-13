using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Strategies.Tests;

public class SmokeTests
{
    [Fact]
    public void AddQuantBacktestStrategies_ReturnsTheSameCollection()
    {
        var services = new ServiceCollection();

        services.AddQuantBacktestStrategies().Should().BeSameAs(services);
    }
}
