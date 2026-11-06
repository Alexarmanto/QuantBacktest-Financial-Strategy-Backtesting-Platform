using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>Records every backtest and writes it to the log, then throws the scripted exception (from the given call on) or delegates.</summary>
internal sealed class LoggingBacktestService(CallLog log, IBacktestService inner) : IBacktestService
{
    private Exception? _failure;
    private int _failFrom = int.MaxValue;

    public List<BacktestRequest> Requests { get; } = [];

    /// <param name="fromCall">1 for the first backtest (training), 2 for the second (test).</param>
    public LoggingBacktestService Failing(Exception exception, int fromCall = 1)
    {
        _failure = exception;
        _failFrom = fromCall;
        return this;
    }

    public Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        log.Add($"Backtest {request.From:yyyy-MM-dd}..{request.To:yyyy-MM-dd}");
        return _failure is not null && Requests.Count >= _failFrom
            ? Task.FromException<BacktestResult>(_failure)
            : inner.RunAsync(request, cancellationToken);
    }
}
