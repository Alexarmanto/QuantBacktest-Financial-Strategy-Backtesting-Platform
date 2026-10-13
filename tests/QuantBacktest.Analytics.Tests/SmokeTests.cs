using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Analytics.Tests;

public class SmokeTests
{
    [Fact]
    public void AddQuantBacktestAnalytics_ReturnsTheSameCollection()
    {
        var services = new ServiceCollection();

        services.AddQuantBacktestAnalytics().Should().BeSameAs(services);
    }
}
