using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// For the data pages (P4-U10): delegates to the Fakes (UNKNOWN, NOKEY, LIMIT, UNCACHED raise their real exceptions)
/// unless a method is scripted, and logs every call in order ("Cached", "Quality AAPL Daily", "Quota",
/// "Series AAPL Daily refresh=True"), so a test can check the order and that GetSeriesAsync is never called.
/// A new double rather than RecordingMarketDataService, which hundreds of existing tests use as it is.
/// </summary>
internal sealed class ScriptedMarketDataService : IMarketDataService
{
    private readonly FakeMarketDataService _inner = new();
    private readonly Dictionary<(string Symbol, Timeframe Timeframe), DataQualityReport> _reports = [];
    private readonly Dictionary<(string Symbol, Timeframe Timeframe), Exception> _qualityFailures = [];
    private IReadOnlyList<string>? _cachedSymbols;
    private ApiQuotaStatus? _quota;
    private PriceSeries? _series;
    private Exception? _seriesFailure;

    public List<string> Calls { get; } = [];

    public IEnumerable<string> SeriesCalls => Calls.Where(c => c.StartsWith("Series", StringComparison.Ordinal));

    public ScriptedMarketDataService WithCachedSymbols(params string[] symbols)
    {
        _cachedSymbols = symbols;
        return this;
    }

    public ScriptedMarketDataService WithReport(DataQualityReport report)
    {
        _reports[(report.Symbol, report.Timeframe)] = report;
        return this;
    }

    public ScriptedMarketDataService WithQualityFailure(string symbol, Timeframe timeframe, Exception exception)
    {
        _qualityFailures[(symbol, timeframe)] = exception;
        return this;
    }

    public ScriptedMarketDataService WithQuota(int usedToday, int dailyLimit = 25)
    {
        _quota = new ApiQuotaStatus(usedToday, dailyLimit, new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));
        return this;
    }

    public ScriptedMarketDataService WithSeries(PriceSeries series)
    {
        _series = series;
        return this;
    }

    public ScriptedMarketDataService WithSeriesFailure(Exception exception)
    {
        _seriesFailure = exception;
        return this;
    }

    public async Task<PriceSeries> GetSeriesAsync(string symbol, Timeframe timeframe, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Series {symbol} {timeframe} refresh={forceRefresh}");
        if (_seriesFailure is not null)
        {
            throw _seriesFailure;
        }

        return _series ?? await _inner.GetSeriesAsync(symbol, timeframe, forceRefresh, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("Cached");
        return _cachedSymbols ?? await _inner.GetCachedSymbolsAsync(cancellationToken);
    }

    public async Task<DataQualityReport> GetQualityReportAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Quality {symbol} {timeframe}");
        if (_qualityFailures.TryGetValue((symbol, timeframe), out var failure))
        {
            throw failure;
        }

        return _reports.TryGetValue((symbol, timeframe), out var report)
            ? report
            : await _inner.GetQualityReportAsync(symbol, timeframe, cancellationToken);
    }

    public async Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("Quota");
        return _quota ?? await _inner.GetApiQuotaAsync(cancellationToken);
    }
}
