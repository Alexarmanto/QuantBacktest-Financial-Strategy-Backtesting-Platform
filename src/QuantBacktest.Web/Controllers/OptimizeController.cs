using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Optimisation heatmap (P4-X2, docs/CONTRACT.md section 6). Thin: BacktestQuery reads and writes the URL,
/// OptimizationQuery checks the axes against the catalog, the view models format, IParameterSweepService computes
/// every cell in one call. Nothing financial happens here.
/// </summary>
public sealed class OptimizeController(
    IStrategyCatalog catalog,
    IMarketDataService marketData,
    ILogger<OptimizeController> logger) : Controller
{
    /// <summary>GET /Optimize: the form, prefilled from the URL parameters present (the "Optimize this strategy" button). Never runs a sweep.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await FormAsync([], alert: null, cancellationToken);

    /// <summary>
    /// GET /Optimize/Result?…: validates the URL, completes its defaults and redirects once to its canonical form, then
    /// calls IParameterSweepService.RunAsync once. Form errors, nothing run: invalid settings, unknown strategy,
    /// unsupported timeframe, fewer than 2 parameters, invalid axes, more than Conventions.MaxSweepCells cells.
    /// BacktestValidationException (the service refusing the request, or an invalid stored custom strategy) and
    /// MarketDataException: the form with the alert of P4-U12. An invalid combination is a cell of the result (O2),
    /// not an exception. Nothing else is caught: any other exception is a bug (/Home/Error).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Result([FromServices] IParameterSweepService sweeps, CancellationToken cancellationToken)
    {
        if (Request.Query.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!BacktestQuery.TryParseOptimization(Request.Query, out var query, out var errors))
        {
            return await FormAsync(errors, alert: null, cancellationToken);
        }

        IStrategy? strategy;
        OptimizationQuery? effective;
        SweepResult result;
        try
        {
            // Inside the try: FindAsync throws BacktestValidationException for a stored custom definition that has
            // become invalid (PART-2 P2-C1), shown like any other refused request.
            strategy = await catalog.FindAsync(query.StrategyId, cancellationToken);
            if (strategy is null)
            {
                return await FormAsync(
                    [new QueryFieldError(BacktestQuery.StrategyKey, StrategyChecks.UnknownStrategy(query.StrategyId))],
                    alert: null,
                    cancellationToken);
            }

            var strategyErrors = query.Resolve(strategy, out effective).ToList();
            if (StrategyChecks.TimeframeError(strategy, query.Timeframe) is { } timeframeError)
            {
                strategyErrors.Insert(0, new QueryFieldError(BacktestQuery.StrategyKey, timeframeError));
            }

            if (strategyErrors.Count > 0 || effective is null)
            {
                return await FormAsync(strategyErrors, alert: null, cancellationToken);
            }

            if (!effective.IsCanonical(Request.QueryString))
            {
                return Redirect(ActionPath + "?" + effective.ToQueryString());
            }

            result = await sweeps.RunAsync(effective.ToSweepRequest(), cancellationToken);
        }
        catch (BacktestValidationException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }
        catch (MarketDataException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }

        var permalink = $"{Request.Scheme}://{Request.Host}{ActionPath}?{effective.ToQueryString()}";
        return View(OptimizeResultViewModel.Create(result, strategy, effective, permalink, PathBase));
    }

    /// <summary>Path of the current action, as received: a redirect keeps it and only rewrites the query string.</summary>
    private string ActionPath => $"{Request.PathBase}{Request.Path}";

    private string PathBase => Request.PathBase.Value ?? string.Empty;

    /// <summary>The form (view Index) with the raw input of the request, its field errors and an optional alert.</summary>
    private async Task<ViewResult> FormAsync(
        IReadOnlyList<QueryFieldError> errors,
        FormAlert? alert,
        CancellationToken cancellationToken)
    {
        var strategies = await catalog.GetAllAsync(cancellationToken);
        var cachedSymbols = await CachedSymbols.GetAsync(marketData, logger, cancellationToken);
        var model = OptimizeFormViewModel.Create(Request.Query, strategies, cachedSymbols, errors, alert);
        return View(nameof(Index), model);
    }
}
