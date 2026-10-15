using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.AlphaVantage;
using QuantBacktest.Data.Options;
using QuantBacktest.Data.Storage;

namespace QuantBacktest.Data;

/// <summary>
/// Part 1 implementation of <see cref="IMarketDataService"/>: cache first, then seed file, then Alpha Vantage
/// (D2, D3, D4, D8, P1-D9 to P1-D12).
/// </summary>
internal sealed partial class MarketDataService(
    PriceCache cache,
    ApiUsageStore usage,
    SeedFiles seedFiles,
    AlphaVantageClient client,
    IOptions<AlphaVantageOptions> options,
    TimeProvider time,
    ILogger<MarketDataService> logger) : IMarketDataService
{
    private readonly ConcurrentDictionary<(string Symbol, Timeframe Timeframe), SeriesGate> _gates = new();
    private readonly SemaphoreSlim _quotaLock = new(1, 1);

    public async Task<PriceSeries> GetSeriesAsync(
        string symbol,
        Timeframe timeframe,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol);
        var key = (normalized, timeframe);
        var gate = _gates.GetOrAdd(key, _ => new SeriesGate());

        Task<PriceSeries> download;
        await gate.Lock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh)
            {
                // D2: cached data is returned as is, without any API call.
                var cached = await cache.GetAsync(normalized, timeframe, cancellationToken);
                if (cached is not null)
                {
                    return cached.Series;
                }

                var seed = await seedFiles.TryReadAsync(normalized, timeframe, cancellationToken);
                if (seed is not null)
                {
                    logger.LogInformation("Importing the seed file of {Symbol} {Timeframe}.", normalized, timeframe);
                    var imported = await cache.MergeAsync(
                        normalized, timeframe, seed.Bars, seed.IsAdjusted, DataOrigin.Seed, time.GetUtcNow(), cancellationToken);
                    return imported.Series;
                }
            }

            // D4, P1-D11: simultaneous calls for the same series share one download.
            download = gate.Download ??= DownloadAndMergeAsync(gate, normalized, timeframe);
        }
        finally
        {
            gate.Lock.Release();
        }

        return await download.WaitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default)
        => cache.GetSymbolsAsync(cancellationToken);

    public Task<DataQualityReport> GetQualityReportAsync(
        string symbol,
        Timeframe timeframe,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The data quality report comes with PR 5 of docs/parts/PART-1.md.");

    /// <summary>D8: requests sent today (UTC), the configured limit and the next UTC midnight.</summary>
    public async Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var used = await usage.GetCountAsync(UtcDay(now), cancellationToken);
        var nextMidnight = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        return new ApiQuotaStatus(used, options.Value.DailyRequestLimit, nextMidnight);
    }

    /// <summary>
    /// Runs without the caller's token: other callers may be waiting for the same download.
    /// A failure leaves the cache untouched (D3).
    /// </summary>
    private async Task<PriceSeries> DownloadAndMergeAsync(SeriesGate gate, string symbol, Timeframe timeframe)
    {
        await Task.Yield();
        try
        {
            await ReserveRequestAsync();
            var downloaded = await client.FetchAsync(symbol, timeframe, CancellationToken.None);
            var merged = await cache.MergeAsync(
                symbol, timeframe, downloaded.Bars, downloaded.IsAdjusted, DataOrigin.Api, time.GetUtcNow(), CancellationToken.None);
            return merged.Series;
        }
        finally
        {
            await gate.Lock.WaitAsync(CancellationToken.None);
            gate.Download = null;
            gate.Lock.Release();
        }
    }

    /// <summary>
    /// D4: quota first, then the key; the request is counted before it is sent, so a failed request counts too (P1-D10).
    /// </summary>
    private async Task ReserveRequestAsync()
    {
        await _quotaLock.WaitAsync(CancellationToken.None);
        try
        {
            var today = UtcDay(time.GetUtcNow());
            var limit = options.Value.DailyRequestLimit;
            if (await usage.GetCountAsync(today, CancellationToken.None) >= limit)
            {
                throw new MarketDataException(
                    MarketDataErrorKind.RateLimitExceeded,
                    $"The daily limit of {limit} Alpha Vantage requests has been reached. It resets at midnight UTC.");
            }

            if (!client.HasApiKey)
            {
                throw AlphaVantageClient.MissingApiKey();
            }

            await usage.IncrementAsync(today, CancellationToken.None);
        }
        finally
        {
            _quotaLock.Release();
        }
    }

    /// <summary>P1-D9: trim, upper case, then <see cref="Conventions.SymbolPattern"/>.</summary>
    private static string Normalize(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!SymbolRegex().IsMatch(normalized))
        {
            throw new MarketDataException(MarketDataErrorKind.SymbolNotFound, "The symbol is not valid.");
        }

        return normalized;
    }

    private static DateOnly UtcDay(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);

    [GeneratedRegex(Conventions.SymbolPattern)]
    private static partial Regex SymbolRegex();

    /// <summary>P1-D11: one lock per (symbol, timeframe), and the download in progress for that series.</summary>
    private sealed class SeriesGate
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public Task<PriceSeries>? Download { get; set; }
    }
}
