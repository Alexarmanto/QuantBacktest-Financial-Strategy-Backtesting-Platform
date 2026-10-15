using Microsoft.Extensions.Logging;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Options;
using QuantBacktest.Data.Storage;

namespace QuantBacktest.Data.Tests.TestSupport;

/// <summary>A real MarketDataService on a temporary database and seed folder, with a fake HTTP endpoint and clock.</summary>
internal sealed class MarketDataHarness : IDisposable
{
    public const string ApiKey = "SECRETKEY123";

    public const string DailyCsv = "timestamp,open,high,low,close,volume\r\n"
        + "2026-01-07,12,13,11,12.5,300\r\n"
        + "2026-01-06,11,12,10,11.5,200\r\n";

    private readonly TemporaryDatabase _database = new();

    public MarketDataHarness(CountingHttpMessageHandler? handler = null, string apiKey = ApiKey, int dailyLimit = 25)
    {
        Handler = handler ?? CountingHttpMessageHandler.Returning(DailyCsv);
        SeedDirectory = Path.Combine(_database.Directory, "seed");
        Directory.CreateDirectory(SeedDirectory);
        Options = new AlphaVantageOptions { ApiKey = apiKey, BaseUrl = "https://example.test/query", DailyRequestLimit = dailyLimit };
        Database = _database.Open();
        Cache = new PriceCache(Database);
        Usage = new ApiUsageStore(Database);
        Service = CreateService();
    }

    public CountingHttpMessageHandler Handler { get; }

    public string SeedDirectory { get; }

    public AlphaVantageOptions Options { get; }

    public SqliteDatabase Database { get; }

    public PriceCache Cache { get; }

    public ApiUsageStore Usage { get; }

    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero));

    public RecordingLoggerProvider Logs { get; } = new();

    public MarketDataService Service { get; }

    public void WriteSeed(string fileName, string csv) => File.WriteAllText(Path.Combine(SeedDirectory, fileName), csv);

    public void Dispose() => _database.Dispose();

    private MarketDataService CreateService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var parser = new AlphaVantageCsvParser(Logs.CreateLogger<AlphaVantageCsvParser>());
        var client = new AlphaVantageClient(new HttpClient(Handler), options, parser, Logs.CreateLogger<AlphaVantageClient>());
        return new MarketDataService(
            Cache,
            Usage,
            new SeedFiles(SeedDirectory, parser),
            client,
            options,
            Time,
            Logs.CreateLogger<MarketDataService>());
    }
}
