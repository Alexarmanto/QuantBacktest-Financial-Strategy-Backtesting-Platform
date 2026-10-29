using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Engine;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IBacktestEngine, IBacktestService and IParameterSweepService as singletons (docs/CONTRACT.md section 2).
    /// </summary>
    public static IServiceCollection AddQuantBacktestEngine(this IServiceCollection services)
    {
        services.AddSingleton<IBacktestEngine, BacktestEngine>();
        services.AddSingleton<IBacktestService, BacktestService>();
        return services;
    }
}
