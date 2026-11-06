using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// Records every request, then: throws the scripted exception, or returns the scripted result, or delegates to the
/// inner service. With a market data service, it first loads the series as the real sweep does (O2, "les données sont
/// chargées une seule fois"), so the error symbols of the Fakes (UNKNOWN, NOKEY, LIMIT) raise their real exceptions.
/// </summary>
internal sealed class RecordingSweepService(IParameterSweepService inner, IMarketDataService? marketData = null) : IParameterSweepService
{
    private Exception? _failure;
    private Func<SweepRequest, SweepResult>? _result;

    public List<SweepRequest> Requests { get; } = [];

    public RecordingSweepService Failing(Exception exception)
    {
        _failure = exception;
        return this;
    }

    public RecordingSweepService Returning(Func<SweepRequest, SweepResult> result)
    {
        _result = result;
        return this;
    }

    public async Task<SweepResult> RunAsync(SweepRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (_failure is not null)
        {
            throw _failure;
        }

        if (marketData is not null)
        {
            await marketData.GetSeriesAsync(request.BaseRequest.Symbol, request.BaseRequest.Timeframe, false, cancellationToken);
        }

        return _result is not null ? _result(request) : await inner.RunAsync(request, cancellationToken);
    }
}
