using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// Records every request, then throws the exception scripted for its strategy id, or delegates to the inner service.
/// Lets the comparator tests check the calls (C1) and make one strategy fail (C-5).
/// </summary>
internal sealed class ScriptedBacktestService(IBacktestService inner) : IBacktestService
{
    private readonly Dictionary<string, Exception> _failures = new(StringComparer.Ordinal);

    public List<BacktestRequest> Requests { get; } = [];

    public ScriptedBacktestService Failing(string strategyId, Exception exception)
    {
        _failures[strategyId] = exception;
        return this;
    }

    public Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return _failures.TryGetValue(request.StrategyId, out var exception)
            ? Task.FromException<BacktestResult>(exception)
            : inner.RunAsync(request, cancellationToken);
    }
}
