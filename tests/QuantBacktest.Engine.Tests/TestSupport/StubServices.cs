using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine.Tests.TestSupport;

/// <summary>A data service that returns one series (or throws), and remembers how it was called.</summary>
internal sealed class StubMarketDataService : IMarketDataService
{
    private readonly PriceSeries _series;

    public StubMarketDataService(PriceSeries series)
    {
        _series = series;
    }

    public MarketDataException? ExceptionToThrow { get; set; }

    public int CallCount { get; private set; }

    public string? LastSymbol { get; private set; }

    public bool LastForceRefresh { get; private set; }

    public Task<PriceSeries> GetSeriesAsync(
        string symbol, Timeframe timeframe, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastSymbol = symbol;
        LastForceRefresh = forceRefresh;
        if (ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(_series);
    }

    public Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<DataQualityReport> GetQualityReportAsync(
        string symbol, Timeframe timeframe, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}

/// <summary>A catalog that holds the strategies given to it.</summary>
internal sealed class StubStrategyCatalog : IStrategyCatalog
{
    private readonly List<IStrategy> _strategies;

    public StubStrategyCatalog(params IStrategy[] strategies)
    {
        _strategies = strategies.ToList();
    }

    public int FindCount { get; private set; }

    public Task<IReadOnlyList<IStrategy>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<IStrategy>>(_strategies);
    }

    public Task<IStrategy?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        FindCount++;
        return Task.FromResult(_strategies.FirstOrDefault(strategy => strategy.Id == id));
    }
}
