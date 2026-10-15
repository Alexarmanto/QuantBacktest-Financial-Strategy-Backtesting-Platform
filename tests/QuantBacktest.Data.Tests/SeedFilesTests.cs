using QuantBacktest.Core.Models;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public class SeedFilesTests
{
    [Theory] // P1-D13, D1: the 8 committed seed files exist and parse
    [InlineData("AAPL")]
    [InlineData("MSFT")]
    [InlineData("SPY")]
    [InlineData("JPM")]
    public async Task CommittedSeedFiles_ParseForBothTimeframes(string symbol)
    {
        var logs = new RecordingLoggerProvider();
        var seeds = new SeedFiles(FindSeedDirectory(), new AlphaVantageCsvParser(logs.CreateLogger<AlphaVantageCsvParser>()));

        var daily = await seeds.TryReadAsync(symbol, Timeframe.Daily, CancellationToken.None);
        var weekly = await seeds.TryReadAsync(symbol, Timeframe.Weekly, CancellationToken.None);

        daily!.Bars.Should().HaveCountGreaterThanOrEqualTo(100);
        daily.IsAdjusted.Should().BeFalse();
        weekly!.Bars.Should().HaveCountGreaterThan(1000);
        weekly.IsAdjusted.Should().BeTrue();
        logs.Entries.Should().BeEmpty("the committed files contain no invalid line");
    }

    [Fact]
    public async Task TryRead_MissingFile_ReturnsNull() // D2: no seed file, the API is used
    {
        var seeds = new SeedFiles(FindSeedDirectory(), new AlphaVantageCsvParser(new RecordingLoggerProvider().CreateLogger<AlphaVantageCsvParser>()));

        (await seeds.TryReadAsync("NOPE", Timeframe.Daily, CancellationToken.None)).Should().BeNull();
    }

    private static string FindSeedDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data", "seed");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("data/seed was not found above the test output folder.");
    }
}
