using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Data.Tests;

public class SmokeTests
{
    [Fact]
    public void AddQuantBacktestData_ReturnsTheSameCollection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddQuantBacktestData(configuration).Should().BeSameAs(services);
    }
}
