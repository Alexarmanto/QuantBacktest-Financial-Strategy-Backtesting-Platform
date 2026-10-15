using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Options;
using QuantBacktest.Data.Storage;

namespace QuantBacktest.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IMarketDataService and ICustomStrategyStore as singletons (docs/CONTRACT.md section 2).
    /// For now: options, the Alpha Vantage client and the SQLite cache; the two services come with PR 3 and PR 4 (docs/parts/PART-1.md section 9).
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

        // P1-D1, P1-D8: the database path is resolved from the content root of the host.
        services.AddSingleton(provider => new SqliteDatabase(DataOptions.ResolvePath(
            provider.GetRequiredService<IHostEnvironment>().ContentRootPath,
            provider.GetRequiredService<IOptions<DataOptions>>().Value.DatabasePath)));
        services.AddSingleton<PriceCache>();

        return services;
    }
}
