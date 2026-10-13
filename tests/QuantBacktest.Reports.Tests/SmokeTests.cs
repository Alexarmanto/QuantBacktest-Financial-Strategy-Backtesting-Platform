using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Reports.Tests;

public class SmokeTests
{
    [Fact]
    public void AddQuantBacktestReports_ReturnsTheSameCollection()
    {
        var services = new ServiceCollection();

        services.AddQuantBacktestReports().Should().BeSameAs(services);
    }
}
