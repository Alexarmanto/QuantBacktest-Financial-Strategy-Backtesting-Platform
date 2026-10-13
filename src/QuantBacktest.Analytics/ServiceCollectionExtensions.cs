using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Analytics;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IMonteCarloService as a singleton (docs/CONTRACT.md section 2).
    /// TODO(part2): implement. The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestAnalytics(this IServiceCollection services)
    {
        return services;
    }
}
