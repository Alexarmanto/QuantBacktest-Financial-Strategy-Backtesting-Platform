using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuantBacktest.Core.Abstractions;
using QuestPDF.Infrastructure;

namespace QuantBacktest.Reports;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IReportGenerator as a singleton (docs/CONTRACT.md section 2).
    /// The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestReports(this IServiceCollection services)
    {
        // P1-R1: QuestPDF Community license, set once here.
        QuestPDF.Settings.License = LicenseType.Community;

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IReportGenerator, PdfReportGenerator>();
        return services;
    }
}
