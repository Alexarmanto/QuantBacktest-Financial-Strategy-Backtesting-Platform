using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Strategies;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IStrategyCatalog and ICustomStrategyFactory as singletons (docs/CONTRACT.md section 2).
    /// The signature is part of the contract and must not change.
    /// Until the no-code rules are added (PR 6), only the catalog is registered (PART-2 section 9).
    /// </summary>
    public static IServiceCollection AddQuantBacktestStrategies(this IServiceCollection services)
    {
        services.AddSingleton<IStrategyCatalog, StrategyCatalog>();
        return services;
    }
}
