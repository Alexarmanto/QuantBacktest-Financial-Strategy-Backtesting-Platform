using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Strategies;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IStrategyCatalog and ICustomStrategyFactory as singletons (docs/CONTRACT.md section 2).
    /// The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestStrategies(this IServiceCollection services)
    {
        services.AddSingleton<IStrategyCatalog, StrategyCatalog>();
        services.AddSingleton<ICustomStrategyFactory, RuleStrategyFactory>();
        return services;
    }
}
