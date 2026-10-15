using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace QuantBacktest.Data.Storage;

/// <summary>
/// Owns the SQLite file (P1-D1, P1-D8): creates its folder and schema once, thread-safely, then hands out contexts.
/// Registered as a singleton; nothing outside this project sees the DbContext.
/// </summary>
internal sealed class SqliteDatabase : IDisposable
{
    private readonly DbContextOptions<QuantBacktestDbContext> _options;
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private volatile bool _created;

    /// <param name="databasePath">An absolute path (see <see cref="Options.DataOptions.ResolvePath"/>).</param>
    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = databasePath;
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        _options = new DbContextOptionsBuilder<QuantBacktestDbContext>().UseSqlite(connectionString).Options;
    }

    public string DatabasePath { get; }

    /// <summary>A new context on a database whose schema exists. The caller disposes it.</summary>
    public async Task<QuantBacktestDbContext> CreateContextAsync(CancellationToken cancellationToken)
    {
        await EnsureCreatedAsync(cancellationToken);
        return new QuantBacktestDbContext(_options);
    }

    public void Dispose() => _creationLock.Dispose();

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_created)
        {
            return;
        }

        await _creationLock.WaitAsync(cancellationToken);
        try
        {
            if (_created)
            {
                return;
            }

            // P1-D1: the folder of the database is created when missing.
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var context = new QuantBacktestDbContext(_options);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            _created = true;
        }
        finally
        {
            _creationLock.Release();
        }
    }
}
