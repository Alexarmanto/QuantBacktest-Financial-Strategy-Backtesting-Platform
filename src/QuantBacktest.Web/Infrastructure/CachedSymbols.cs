using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Web.Infrastructure;

/// <summary>The symbols suggested by the datalist of the forms (P4-U1). Shared by the backtest form and the comparator.</summary>
internal static class CachedSymbols
{
    /// <summary>The datalist is a convenience: if the cache cannot be listed, the form still works without it.</summary>
    public static async Task<IReadOnlyList<string>> GetAsync(
        IMarketDataService marketData,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(marketData);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            return await marketData.GetCachedSymbolsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Cached symbols could not be listed; the symbol suggestions are left empty.");
            return [];
        }
    }
}
