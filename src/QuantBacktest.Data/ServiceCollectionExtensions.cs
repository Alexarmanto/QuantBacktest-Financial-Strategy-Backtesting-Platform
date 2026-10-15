using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Options;

namespace QuantBacktest.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IMarketDataService and ICustomStrategyStore as singletons (docs/CONTRACT.md section 2).
    /// For now: options and the Alpha Vantage client; the two services come with PR 3 and PR 4 (docs/parts/PART-1.md section 9).
    /// The signature is part of the contract and must not change.
    /// </summary>
    public static IServiceCollection AddQuantBacktestData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // P1-D1
        services.Configure<AlphaVantageOptions>(configuration.GetSection(AlphaVantageOptions.SectionName));
        services.Configure<DataOptions>(configuration.GetSection(DataOptions.SectionName));

        // P1-D2: without RemoveAllLoggers, .NET logs every request URL, and the URL contains the key.
        services.AddSingleton<AlphaVantageCsvParser>();
        services.AddHttpClient<AlphaVantageClient>(
                AlphaVantageClient.HttpClientName,
                client => client.Timeout = AlphaVantageClient.Timeout)
            .RemoveAllLoggers();

        return services;
    }
}
