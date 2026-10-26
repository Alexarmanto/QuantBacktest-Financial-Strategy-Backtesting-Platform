using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>
/// The entry point of one backtest: finds the strategy, validates the request, loads the prices and runs the engine.
/// A MarketDataException from the data service is not caught: it goes straight to the caller.
/// </summary>
internal sealed class BacktestService : IBacktestService
{
    private readonly IMarketDataService _marketData;
    private readonly IStrategyCatalog _catalog;
    private readonly IBacktestEngine _engine;

    public BacktestService(IMarketDataService marketData, IStrategyCatalog catalog, IBacktestEngine engine)
    {
        _marketData = marketData;
        _catalog = catalog;
        _engine = engine;
    }

    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IStrategy strategy = await _catalog.FindAsync(request.StrategyId, cancellationToken)
            ?? throw new BacktestValidationException($"Unknown strategy '{request.StrategyId}'.");

        BacktestRequest completed = RequestValidator.ValidateAndComplete(request, strategy);

        string symbol = RequestValidator.NormalizeSymbol(request.Symbol);
        PriceSeries series = await _marketData.GetSeriesAsync(symbol, request.Timeframe, false, cancellationToken);

        return _engine.Run(completed, series, strategy);
    }
}
