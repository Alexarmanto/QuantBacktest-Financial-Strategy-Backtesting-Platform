using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Abstractions;

/// <summary>
/// Part 3. The single entry point used by the web layer for one backtest: resolves the strategy,
/// validates the whole request, loads data (never forcing a refresh), runs the engine.
/// </summary>
public interface IBacktestService
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default);
}
