using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Strategies;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IStrategyCatalog and ICustomStrategyFactory as singletons (docs/CONTRACT.md section 2).
    /// TODO(part2): implement. The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestStrategies(this IServiceCollection services)
    {
        return services;
    }
}
