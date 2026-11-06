using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// Records every GetSeriesAsync (and writes it to the log, if any), then throws the scripted exception, or returns the
/// scripted series, or delegates to the inner service (the Fakes: UNKNOWN, NOKEY, LIMIT raise their real exceptions).
/// The other methods delegate.
/// </summary>
internal sealed class RecordingMarketDataService(IMarketDataService inner, CallLog? log = null, PriceSeries? series = null) : IMarketDataService
{
    private Exception? _failure;

    public List<(string Symbol, Timeframe Timeframe, bool ForceRefresh)> SeriesRequests { get; } = [];

    public RecordingMarketDataService Failing(Exception exception)
    {
        _failure = exception;
        return this;
    }

    public async Task<PriceSeries> GetSeriesAsync(string symbol, Timeframe timeframe, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        SeriesRequests.Add((symbol, timeframe, forceRefresh));
        log?.Add($"GetSeries {symbol} {timeframe} refresh={forceRefresh}");
        if (_failure is not null)
        {
            throw _failure;
        }

        return series ?? await inner.GetSeriesAsync(symbol, timeframe, forceRefresh, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default) =>
        inner.GetCachedSymbolsAsync(cancellationToken);

    public Task<QuantBacktest.Core.Models.DataQualityReport> GetQualityReportAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
        inner.GetQualityReportAsync(symbol, timeframe, cancellationToken);

    public Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default) =>
        inner.GetApiQuotaAsync(cancellationToken);
}
