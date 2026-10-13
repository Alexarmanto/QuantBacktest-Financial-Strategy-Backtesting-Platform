using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IMarketDataService and ICustomStrategyStore as singletons (docs/CONTRACT.md section 2).
    /// TODO(part1): implement. The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestData(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
