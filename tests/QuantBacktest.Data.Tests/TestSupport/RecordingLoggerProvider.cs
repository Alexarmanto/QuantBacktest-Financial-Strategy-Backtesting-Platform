using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace QuantBacktest.Data.Tests.TestSupport;

/// <summary>Keeps every log entry (category, level, formatted message and exception text) for assertions.</summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    /// <summary>Every log entry as one text, to check that a secret never appears anywhere.</summary>
    public string AllText => string.Join('\n', _entries.Select(entry => $"{entry.Category} {entry.Message} {entry.Exception}"));

    public ILogger<T> CreateLogger<T>() => new Logger<T>(new LoggerFactory([this]));

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    internal sealed record LogEntry(string Category, LogLevel Level, string Message, string? Exception);

    private sealed class RecordingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception?.ToString()));
    }
}
