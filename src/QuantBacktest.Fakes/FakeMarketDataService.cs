using System.Text.RegularExpressions;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>
/// Serves synthetic series for any valid symbol. Special symbols exercise the error paths of the UI:
/// UNKNOWN gives SymbolNotFound, NOKEY gives MissingApiKey, LIMIT gives RateLimitExceeded,
/// and UNCACHED gives NotCached for the quality report.
/// </summary>
public sealed class FakeMarketDataService(TimeProvider? timeProvider = null) : IMarketDataService
{
    private static readonly string[] Symbols = ["AAPL", "JPM", "MSFT", "SPY"];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private int _usedToday = 3;

    public Task<PriceSeries> GetSeriesAsync(
        string symbol,
        Timeframe timeframe,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol);
        if (forceRefresh)
        {
            Interlocked.Increment(ref _usedToday);
        }

        return Task.FromResult(CreateSeries(normalized, timeframe));
    }

    public Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(Symbols);

    public Task<DataQualityReport> GetQualityReportAsync(
        string symbol,
        Timeframe timeframe,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(symbol);
        if (normalized == "UNCACHED")
        {
            throw new MarketDataException(MarketDataErrorKind.NotCached, $"No cached data for {normalized} ({timeframe}).");
        }

        var series = CreateSeries(normalized, timeframe);
        var bars = series.Bars;
        var anomalies = new List<DataAnomaly>
        {
            new(bars[bars.Count / 3].Date, DataAnomalyKind.Gap, "[FAKE] 8 calendar days without data."),
            new(bars[bars.Count / 2].Date, DataAnomalyKind.PriceJump, "[FAKE] Close moved by 31.2 % in one bar (possible split)."),
        };

        var report = new DataQualityReport(
            series.Symbol,
            timeframe,
            bars.Count,
            bars[0].Date,
            bars[^1].Date,
            series.IsAdjusted,
            DataOrigin.Mixed,
            new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero),
            MissingPeriods: 7,
            LargestGapDays: 8,
            anomalies);
        return Task.FromResult(report);
    }

    public Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var resetsAt = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        return Task.FromResult(new ApiQuotaStatus(Volatile.Read(ref _usedToday), 25, resetsAt));
    }

    private static string Normalize(string symbol)
    {
        var normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (!Regex.IsMatch(normalized, Conventions.SymbolPattern))
        {
            throw new MarketDataException(MarketDataErrorKind.SymbolNotFound, $"'{symbol}' is not a valid symbol.");
        }

        return normalized switch
        {
            "UNKNOWN" => throw new MarketDataException(MarketDataErrorKind.SymbolNotFound, $"Symbol '{normalized}' was not found."),
            "NOKEY" => throw new MarketDataException(MarketDataErrorKind.MissingApiKey, "No Alpha Vantage API key is configured."),
            "LIMIT" => throw new MarketDataException(MarketDataErrorKind.RateLimitExceeded, "The daily Alpha Vantage limit (25 requests) is reached."),
            _ => normalized,
        };
    }

    private static PriceSeries CreateSeries(string symbol, Timeframe timeframe)
    {
        var barCount = timeframe == Timeframe.Daily ? 1500 : 520;
        return SyntheticPrices.Generate(symbol, timeframe, barCount, StableSeed(symbol));
    }

    private static int StableSeed(string text)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in text)
            {
                hash = (hash * 31) + character;
            }

            return hash;
        }
    }
}
