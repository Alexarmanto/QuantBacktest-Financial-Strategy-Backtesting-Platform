using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>Part 1. Returns clean price series; hides the API, the cache and the seed files.</summary>
public interface IMarketDataService
{
    /// <summary>
    /// Returns the full available history. Symbol is trimmed and upper-cased.
    /// forceRefresh = false never calls the API when cached data exists (reproducible results);
    /// forceRefresh = true asks the API and merges new bars by date. Algorithm: docs/CONTRACT.md section 7.
    /// Throws <see cref="Errors.MarketDataException"/> when no data can be provided.
    /// </summary>
    Task<PriceSeries> GetSeriesAsync(
        string symbol,
        Timeframe timeframe,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>Symbols that currently have data in the local cache (any timeframe), sorted alphabetically.</summary>
    Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default);

    /// <summary>Quality diagnostics of cached data only; never calls the API. Throws NotCached when nothing is cached.</summary>
    Task<DataQualityReport> GetQualityReportAsync(
        string symbol,
        Timeframe timeframe,
        CancellationToken cancellationToken = default);

    /// <summary>API calls used today (UTC) against the configured daily limit.</summary>
    Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default);
}
