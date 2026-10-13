using Microsoft.Extensions.DependencyInjection;

namespace QuantBacktest.Reports;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IReportGenerator as a singleton (docs/CONTRACT.md section 2).
    /// TODO(part1): implement. The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestReports(this IServiceCollection services)
    {
        return services;
    }
}
