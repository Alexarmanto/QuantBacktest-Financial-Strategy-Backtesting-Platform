using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Engine;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IBacktestEngine, IBacktestService and IParameterSweepService as singletons (docs/CONTRACT.md section 2).
    /// TODO(part3): implement. The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestEngine(this IServiceCollection services)
    {
        return services;
    }
}

// Scope rule test, do not merge.
