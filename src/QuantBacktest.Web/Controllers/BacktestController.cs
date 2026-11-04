using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
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

        try
        {
            // Inside the try: FindAsync throws BacktestValidationException for a stored custom definition that has
            // become invalid (PART-2 P2-C1), which is shown like any other refused request, never as a 500.
            var (strategy, strategyErrors) = await ResolveStrategyAsync(query, cancellationToken);
            if (strategy is null)
            {
                return await FormAsync(strategyErrors, alert: null, cancellationToken);
            }

            // The permalink carries every effective setting, strategy defaults included.
            var effective = query.WithDefaultParameters(strategy.Parameters);
            if (!effective.IsCanonical(Request.QueryString))
            {
                return Redirect(ActionPath + "?" + effective.ToQueryString());
            }

            var result = await backtests.RunAsync(effective.ToBacktestRequest(), cancellationToken);
            var permalink = $"{Request.Scheme}://{Request.Host}{ActionPath}?{effective.ToQueryString()}";
            return View(BacktestResultViewModel.Create(result, strategy, effective, permalink, PathBase));
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

    /// <summary>
    /// GET /Backtest/MonteCarlo?…&amp;sims=&amp;seed= (P4-X4): same reading and canonical redirect as Result, with sims and
    /// seed after the backtest settings (mc is never part of this URL). Reruns the backtest (1 call to
    /// IBacktestService), then IMonteCarloService.Run on its result. Errors: invalid backtest settings or a refused
    /// backtest → the backtest form, like Result; invalid sims / seed → this page with the field error, nothing run;
    /// Monte Carlo refused (BacktestValidationException, MC1) → this page with the message. Nothing else is caught.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MonteCarlo(
        [FromServices] IBacktestService backtests,
        [FromServices] IMonteCarloService monteCarlo,
        CancellationToken cancellationToken)
    {
        if (Request.Query.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!BacktestQuery.TryParse(Request.Query, out var query, out var errors))
        {
            return await FormAsync(errors, alert: null, cancellationToken);
        }

        var monteCarloValid = BacktestQuery.TryParseMonteCarlo(Request.Query, out var monteCarloRequest, out var monteCarloErrors);

        BacktestResult result;
        IStrategy strategy;
        BacktestQuery effective;
        string permalink;
        try
        {
            var (found, strategyErrors) = await ResolveStrategyAsync(query, cancellationToken);
            if (found is null)
            {
                return await FormAsync(strategyErrors, alert: null, cancellationToken);
            }

            strategy = found;
            effective = query.WithDefaultParameters(strategy.Parameters);
            if (!monteCarloValid)
            {
                return View(MonteCarloViewModel.InvalidSettings(strategy, effective, Request.Query, monteCarloErrors, PathBase));
            }

            if (!effective.IsCanonical(Request.QueryString, monteCarloRequest!))
            {
                return Redirect(ActionPath + "?" + effective.ToQueryString(monteCarloRequest!));
            }

            result = await backtests.RunAsync(effective.ToBacktestRequest(), cancellationToken);
            permalink = $"{Request.Scheme}://{Request.Host}{ActionPath}?{effective.ToQueryString(monteCarloRequest!)}";
        }
        catch (BacktestValidationException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }
        catch (MarketDataException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }

        try
        {
            var simulation = monteCarlo.Run(result, monteCarloRequest!);
            return View(MonteCarloViewModel.Create(result, strategy, effective, simulation, permalink, PathBase));
        }
        catch (BacktestValidationException exception)
        {
            // MC1, the only exception IMonteCarloService declares: fewer than 5 trades, or a request out of range.
            return View(MonteCarloViewModel.Refused(result, strategy, effective, monteCarloRequest!, exception, permalink, PathBase));
        }
    }

    /// <summary>Path of the current action, as received: a redirect keeps it and only rewrites the query string.</summary>
    private string ActionPath => $"{Request.PathBase}{Request.Path}";

    private string PathBase => Request.PathBase.Value ?? string.Empty;

    /// <summary>
    /// The strategy of the query, or the field errors to show on the form: unknown strategy, unsupported timeframe
    /// (first), p.* out of the catalog ranges. May throw BacktestValidationException (invalid stored custom strategy).
    /// </summary>
    private async Task<(IStrategy? Strategy, IReadOnlyList<QueryFieldError> Errors)> ResolveStrategyAsync(
        BacktestQuery query,
        CancellationToken cancellationToken)
    {
        var strategy = await catalog.FindAsync(query.StrategyId, cancellationToken);
        if (strategy is null)
        {
            return (null, [new QueryFieldError(BacktestQuery.StrategyKey, $"Unknown strategy '{query.StrategyId}'.")]);
        }

        var strategyErrors = query.ValidateParameters(strategy.Parameters).ToList();
        if (!strategy.SupportedTimeframes.Contains(query.Timeframe))
        {
            strategyErrors.Insert(0, new QueryFieldError(
                BacktestQuery.StrategyKey,
                $"{strategy.Name} does not support the {BacktestQuery.FormatTimeframe(query.Timeframe)} timeframe "
                + $"({StrategyOption.TimeframeRestriction(strategy)})."));
        }

        return strategyErrors.Count > 0 ? (null, strategyErrors) : (strategy, []);
    }

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
