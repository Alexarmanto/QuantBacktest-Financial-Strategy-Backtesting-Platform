using Microsoft.Data.Sqlite;
using QuantBacktest.Data.Storage;

namespace QuantBacktest.Data.Tests.TestSupport;

/// <summary>A SQLite file in a fresh temporary folder (which does not exist yet), deleted on dispose.</summary>
internal sealed class TemporaryDatabase : IDisposable
{
    private readonly List<SqliteDatabase> _opened = [];

    public TemporaryDatabase()
    {
        Directory = Path.Combine(Path.GetTempPath(), "quantbacktest-tests", Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(Directory, "App_Data", "quantbacktest.db");
    }

    public string Directory { get; }

    public string DatabasePath { get; }

    /// <summary>A new SqliteDatabase instance on the same file (as after an application restart).</summary>
    public SqliteDatabase Open()
    {
        var database = new SqliteDatabase(DatabasePath);
        _opened.Add(database);
        return database;
    }

    public void Dispose()
    {
        foreach (var database in _opened)
        {
            database.Dispose();
        }

        // Pooled connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
