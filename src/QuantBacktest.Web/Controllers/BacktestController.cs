using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Backtest form and permalink results (PART-4 section 5, P4-U1, P4-U12). Thin: BacktestQuery reads and writes the
/// URL, the view models format, IBacktestService computes. Nothing financial happens here.
/// </summary>
public sealed class BacktestController(
    IStrategyCatalog catalog,
    IMarketDataService marketData,
    ILogger<BacktestController> logger) : Controller
{
    /// <summary>GET /Backtest: the form, prefilled from the URL parameters present, or with the defaults. Never runs a backtest.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await FormAsync([], alert: null, cancellationToken);

    /// <summary>
    /// GET /Backtest/Result?…: validates the URL, redirects once to its canonical form, then runs the backtest.
    /// Invalid input or a refused request shows the form again with the raw input; it is never a 500.
    /// The engine is injected here only, so the form does not depend on it.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Result([FromServices] IBacktestService backtests, CancellationToken cancellationToken)
    {
        if (Request.Query.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!BacktestQuery.TryParse(Request.Query, out var query, out var errors))
        {
            return await FormAsync(errors, alert: null, cancellationToken);
        }

        var strategy = await catalog.FindAsync(query.StrategyId, cancellationToken);
        if (strategy is null)
        {
            return await FormAsync(
                [new QueryFieldError(BacktestQuery.StrategyKey, $"Unknown strategy '{query.StrategyId}'.")],
                alert: null,
                cancellationToken);
        }

        var strategyErrors = query.ValidateParameters(strategy.Parameters).ToList();
        if (!strategy.SupportedTimeframes.Contains(query.Timeframe))
        {
            strategyErrors.Insert(0, new QueryFieldError(
                BacktestQuery.StrategyKey,
                $"{strategy.Name} does not support the {BacktestQuery.FormatTimeframe(query.Timeframe)} timeframe "
                + $"({StrategyOption.TimeframeRestriction(strategy)})."));
        }

        if (strategyErrors.Count > 0)
        {
            return await FormAsync(strategyErrors, alert: null, cancellationToken);
        }

        // The permalink carries every effective setting, strategy defaults included.
        var effective = query.WithDefaultParameters(strategy.Parameters);
        if (!effective.IsCanonical(Request.QueryString))
        {
            return Redirect(ResultPath + "?" + effective.ToQueryString());
        }

        try
        {
            var result = await backtests.RunAsync(effective.ToBacktestRequest(), cancellationToken);
            var permalink = $"{Request.Scheme}://{Request.Host}{ResultPath}?{effective.ToQueryString()}";
            return View(BacktestResultViewModel.Create(result, strategy, permalink));
        }
        catch (BacktestValidationException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }
        catch (MarketDataException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }
    }

    /// <summary>Path of the current action, as received: a redirect keeps it and only rewrites the query string.</summary>
    private string ResultPath => $"{Request.PathBase}{Request.Path}";

    /// <summary>The form (view Index) with the raw input of the request, its field errors and an optional alert.</summary>
    private async Task<ViewResult> FormAsync(
        IReadOnlyList<QueryFieldError> errors,
        FormAlert? alert,
        CancellationToken cancellationToken)
    {
        var strategies = await catalog.GetAllAsync(cancellationToken);
        var cachedSymbols = await GetCachedSymbolsAsync(cancellationToken);
        var model = BacktestFormViewModel.Create(Request.Query, strategies, cachedSymbols, errors, alert);
        return View(nameof(Index), model);
    }

    /// <summary>The datalist is a convenience: if the cache cannot be listed, the form still works without it.</summary>
    private async Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken)
    {
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
