using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Analytics;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IMonteCarloService as a singleton (docs/CONTRACT.md section 2).
    /// The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestAnalytics(this IServiceCollection services)
    {
        services.AddSingleton<IMonteCarloService, MonteCarloService>();
        return services;
    }
}
