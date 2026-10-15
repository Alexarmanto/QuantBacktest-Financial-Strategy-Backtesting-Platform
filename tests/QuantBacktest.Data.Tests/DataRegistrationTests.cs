using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Options;
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

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuantBacktestData(BuildConfiguration(settings));
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
