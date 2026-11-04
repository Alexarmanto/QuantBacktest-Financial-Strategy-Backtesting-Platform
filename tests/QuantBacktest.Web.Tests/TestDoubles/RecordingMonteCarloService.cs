using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Web.Tests.TestDoubles;

/// <summary>
/// Records every call, then answers with the fake (or throws the given exception). The fake ignores the seed, so the
/// web can only prove that it passes sims and seed through unchanged (decision H5).
/// </summary>
internal sealed class RecordingMonteCarloService(Exception? toThrow = null) : IMonteCarloService
{
    private readonly FakeMonteCarloService _inner = new();

    public List<(BacktestResult Result, MonteCarloRequest Request)> Calls { get; } = [];

    public MonteCarloResult Run(BacktestResult result, MonteCarloRequest request)
    {
        Calls.Add((result, request));
        if (toThrow is not null)
        {
            throw toThrow;
        }

        return _inner.Run(result, request);
    }
}

/// <summary>Counts the backtests, and can cut the trades of the fake result to test MC1 (fewer than 5 trades).</summary>
internal sealed class CountingBacktestService(IBacktestService inner, int? keepTrades = null) : IBacktestService
{
    public int Calls { get; private set; }

    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        var result = await inner.RunAsync(request, cancellationToken);
        return keepTrades is { } count ? result with { Trades = result.Trades.Take(count).ToList() } : result;
    }
}
