using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Fakes;

/// <summary>
/// One method per part, so the web app can mix real parts and fakes (keys QuantBacktest:UseFakes:*).
/// Fakes are NOT authoritative: the real behavior is specified in docs/CONTRACT.md.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Replaces Part 1 (Data): IMarketDataService and ICustomStrategyStore.</summary>
    public static IServiceCollection AddFakeData(this IServiceCollection services)
    {
        services.AddSingleton<IMarketDataService, FakeMarketDataService>();
        services.AddSingleton<ICustomStrategyStore, FakeCustomStrategyStore>();
        return services;
    }

    /// <summary>Replaces Part 1 (Reports): IReportGenerator.</summary>
    public static IServiceCollection AddFakeReports(this IServiceCollection services)
    {
        services.AddSingleton<IReportGenerator, FakeReportGenerator>();
        return services;
    }

    /// <summary>Replaces Part 2 (Strategies): IStrategyCatalog and ICustomStrategyFactory.</summary>
    public static IServiceCollection AddFakeStrategies(this IServiceCollection services)
    {
        services.AddSingleton<IStrategyCatalog, FakeStrategyCatalog>();
        services.AddSingleton<ICustomStrategyFactory, FakeCustomStrategyFactory>();
        return services;
    }

    /// <summary>Replaces Part 2 (Analytics): IMonteCarloService.</summary>
    public static IServiceCollection AddFakeAnalytics(this IServiceCollection services)
    {
        services.AddSingleton<IMonteCarloService, FakeMonteCarloService>();
        return services;
    }

    /// <summary>Replaces Part 3 (Engine): IBacktestService and IParameterSweepService.</summary>
    public static IServiceCollection AddFakeEngine(this IServiceCollection services)
    {
        services.AddSingleton<IBacktestService, FakeBacktestService>();
        services.AddSingleton<IParameterSweepService, FakeParameterSweepService>();
        return services;
    }
}
