using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Engine.Tests;

public class SmokeTests
{
    [Fact]
    public void AddQuantBacktestEngine_ReturnsTheSameCollection()
    {
        var services = new ServiceCollection();

        services.AddQuantBacktestEngine().Should().BeSameAs(services);
    }
}
