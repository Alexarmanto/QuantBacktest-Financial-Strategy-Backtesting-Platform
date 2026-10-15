using Microsoft.EntityFrameworkCore;
using QuantBacktest.Data.Tests.TestSupport;

namespace QuantBacktest.Data.Tests;

public sealed class SqliteDatabaseTests : IDisposable
{
    private readonly TemporaryDatabase _temporary = new();

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task CreateContextAsync_CreatesTheMissingFolderAndTheFourTables() // P1-D1, P1-D8
    {
        Directory.Exists(Path.GetDirectoryName(_temporary.DatabasePath)).Should().BeFalse();

        await using var context = await _temporary.Open().CreateContextAsync(CancellationToken.None);

        File.Exists(_temporary.DatabasePath).Should().BeTrue();
        var tables = await context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table' ORDER BY name")
            .ToListAsync();
        tables.Should().Equal("ApiUsage", "CustomStrategies", "PriceBars", "SeriesMetadata");
    }

    [Fact]
    public async Task CreateContextAsync_CalledConcurrently_CreatesTheSchemaOnce() // P1-D8: thread-safe EnsureCreated
    {
        var database = _temporary.Open();

        var contexts = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => database.CreateContextAsync(CancellationToken.None))));

        foreach (var context in contexts)
        {
            (await context.PriceBars.CountAsync()).Should().Be(0);
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateContextAsync_OnAnExistingFile_KeepsItsData() // P1-D8: no migration, no reset
    {
        await using (var context = await _temporary.Open().CreateContextAsync(CancellationToken.None))
        {
            context.ApiUsage.Add(new() { DateUtc = new DateOnly(2026, 10, 15), Count = 3 });
            await context.SaveChangesAsync();
        }

        await using var reopened = await _temporary.Open().CreateContextAsync(CancellationToken.None);

        (await reopened.ApiUsage.SingleAsync()).Count.Should().Be(3);
    }
}
