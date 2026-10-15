using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Fakes;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Options;
using QuantBacktest.Data.Storage;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public class DataRegistrationTests
{
    private const string ApiKey = "SECRETKEY123";

    [Fact]
    public void Options_WithoutConfiguration_UseTheContractDefaults() // P1-D1, CONTRACT section 13
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        var alphaVantage = provider.GetRequiredService<IOptions<AlphaVantageOptions>>().Value;
        var data = provider.GetRequiredService<IOptions<DataOptions>>().Value;

        alphaVantage.ApiKey.Should().BeEmpty();
        alphaVantage.BaseUrl.Should().Be("https://www.alphavantage.co/query");
        alphaVantage.DailyRequestLimit.Should().Be(25);
        data.DatabasePath.Should().Be("App_Data/quantbacktest.db");
        data.SeedDirectory.Should().Be("../../data/seed");
    }

    [Fact]
    public void Options_AreBoundFromTheirSections() // P1-D1
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AlphaVantage:ApiKey"] = ApiKey,
            ["AlphaVantage:BaseUrl"] = "https://example.test/query",
            ["AlphaVantage:DailyRequestLimit"] = "7",
            ["QuantBacktest:DatabasePath"] = "/data/quantbacktest.db",
            ["QuantBacktest:SeedDirectory"] = "/app/seed",
        });

        var alphaVantage = provider.GetRequiredService<IOptions<AlphaVantageOptions>>().Value;
        var data = provider.GetRequiredService<IOptions<DataOptions>>().Value;

        alphaVantage.ApiKey.Should().Be(ApiKey);
        alphaVantage.BaseUrl.Should().Be("https://example.test/query");
        alphaVantage.DailyRequestLimit.Should().Be(7);
        data.DatabasePath.Should().Be("/data/quantbacktest.db");
        data.SeedDirectory.Should().Be("/app/seed");
    }

    [Fact]
    public void ResolvePath_RelativePath_IsResolvedFromTheContentRoot() // P1-D1
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "qb-content-root");

        var resolved = DataOptions.ResolvePath(contentRoot, "../../data/seed");

        resolved.Should().Be(Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "data", "seed")));
    }

    [Fact]
    public void ResolvePath_AbsolutePath_IsKeptAsIs() // P1-D1
    {
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "quantbacktest.db");

        var resolved = DataOptions.ResolvePath(Path.Combine(Path.GetTempPath(), "qb-content-root"), absolute);

        resolved.Should().Be(Path.GetFullPath(absolute));
    }

    [Fact]
    public void HttpClient_HasA30SecondTimeout() // P1-D2
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AlphaVantageClient.HttpClientName);

        client.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task HttpClient_FromDependencyInjection_NeverLogsTheRequestUrl() // P1-D2: RemoveAllLoggers
    {
        var logs = new RecordingLoggerProvider();
        var handler = CountingHttpMessageHandler.Returning("timestamp,open,high,low,close,volume\n2026-01-05,10,11,9,10.5,100\n");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddQuantBacktestData(BuildConfiguration(new Dictionary<string, string?> { ["AlphaVantage:ApiKey"] = ApiKey }));
        services.AddHttpClient(AlphaVantageClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<AlphaVantageClient>().FetchAsync("MSFT", Timeframe.Daily, CancellationToken.None);

        handler.CallCount.Should().Be(1);
        handler.RequestedUris.Single().Query.Should().Contain(ApiKey);
        logs.Entries.Should().NotContain(entry => entry.Category.StartsWith("System.Net.Http", StringComparison.Ordinal));
        logs.AllText.Should().NotContain(ApiKey);
    }

    [Fact]
    public void Database_RelativePath_IsResolvedFromTheContentRootOfTheHost() // P1-D1, P1-D8
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "qb-content-root");
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services.AddQuantBacktestData(BuildConfiguration(new Dictionary<string, string?>()));
        using var provider = services.BuildServiceProvider();

        var database = provider.GetRequiredService<SqliteDatabase>();

        database.DatabasePath.Should().Be(Path.GetFullPath(Path.Combine(contentRoot, "App_Data", "quantbacktest.db")));
        provider.GetRequiredService<PriceCache>().Should().BeSameAs(provider.GetRequiredService<PriceCache>());
    }

    [Fact]
    public async Task RealServices_FromDependencyInjection_ServeSeedDataAndStoreStrategies() // CONTRACT section 2, D2, P1-D14
    {
        using var temporary = new TemporaryDatabase();
        var seedDirectory = Path.Combine(temporary.Directory, "seed");
        Directory.CreateDirectory(seedDirectory);
        await File.WriteAllTextAsync(Path.Combine(seedDirectory, "MSFT_daily.csv"), "timestamp,open,high,low,close,volume\n2026-01-05,10,11,9,10.5,100\n");
        var handler = CountingHttpMessageHandler.Returning(string.Empty);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Path.Combine(temporary.Directory, "content")));
        services.AddQuantBacktestData(BuildConfiguration(new Dictionary<string, string?>
        {
            ["QuantBacktest:DatabasePath"] = temporary.DatabasePath,
            ["QuantBacktest:SeedDirectory"] = "../seed",
        }));
        services.AddHttpClient(AlphaVantageClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using (var provider = services.BuildServiceProvider())
        {
            var marketData = provider.GetRequiredService<IMarketDataService>();
            var store = provider.GetRequiredService<ICustomStrategyStore>();
            marketData.Should().BeSameAs(provider.GetRequiredService<IMarketDataService>());
            store.Should().BeSameAs(provider.GetRequiredService<ICustomStrategyStore>());

            (await marketData.GetSeriesAsync("msft", Timeframe.Daily)).Bars.Should().ContainSingle();
            (await marketData.GetCachedSymbolsAsync()).Should().Equal("MSFT");
            (await marketData.GetQualityReportAsync("MSFT", Timeframe.Daily)).Origin.Should().Be(DataOrigin.Seed);
            (await marketData.GetApiQuotaAsync()).UsedToday.Should().Be(0);
            await store.SaveAsync(FakeCustomStrategyStore.Example);
            (await store.GetAllAsync()).Should().ContainSingle();
        }

        handler.CallCount.Should().Be(0);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuantBacktestData(BuildConfiguration(settings));
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "QuantBacktest.Data.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
