using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Web.Infrastructure;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Strategy comparator (P4-X1, docs/CONTRACT.md C1). Thin: BacktestQuery reads and writes the URL, the view models
/// format, IBacktestService computes, once per strategy, one after the other (the contract does not say the service
/// is safe to call in parallel). Nothing financial happens here.
/// </summary>
public sealed class CompareController(
    IStrategyCatalog catalog,
    IMarketDataService marketData,
    ILogger<CompareController> logger) : Controller
{
    /// <summary>GET /Compare: the form, prefilled from the URL parameters present. Never runs a backtest.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await FormAsync([], alert: null, cancellationToken);

    /// <summary>
    /// GET /Compare/Result?…&amp;strategies=id1,id2,…: validates the URL, redirects once to its canonical form, then
    /// runs one backtest per strategy, in the order of the URL, each with its default parameters (P4-X1).
    /// Form errors, nothing run: invalid settings, fewer than 2 or more than MaxComparedStrategies ids, a duplicate.
    /// Errors of one strategy, the others still shown (C-5): unknown id, unsupported timeframe (both checked here, as on
    /// /Backtest, without calling the service), BacktestValidationException of the catalog or of the service.
    /// MarketDataException: the data are shared by every strategy, so the first one stops the comparison and the form
    /// shows the alert of P4-U12. Nothing else is caught: any other exception is a bug (/Home/Error).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Result([FromServices] IBacktestService backtests, CancellationToken cancellationToken)
    {
        if (Request.Query.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!BacktestQuery.TryParseComparison(Request.Query, out var query, out var errors))
        {
            return await FormAsync(errors, alert: null, cancellationToken);
        }

        if (!query.IsCanonical(Request.QueryString))
        {
            return Redirect(ActionPath + "?" + query.ToQueryString());
        }

        var outcomes = new List<ComparisonOutcome>(query.StrategyIds.Count);
        foreach (var id in query.StrategyIds)
        {
            IStrategy? strategy;
            try
            {
                // FindAsync throws BacktestValidationException for a stored custom definition that has become invalid.
                strategy = await catalog.FindAsync(id, cancellationToken);
            }
            catch (BacktestValidationException exception)
            {
                outcomes.Add(ComparisonOutcome.Failure(id, id, exception.Message));
                continue;
            }

            if (strategy is null)
            {
                outcomes.Add(ComparisonOutcome.Failure(id, id, StrategyChecks.UnknownStrategy(id)));
                continue;
            }

            if (StrategyChecks.TimeframeError(strategy, query.Timeframe) is { } timeframeError)
            {
                outcomes.Add(ComparisonOutcome.Failure(id, strategy.Name, timeframeError));
                continue;
            }

            var effective = query.ForStrategy(strategy);
            try
            {
                var result = await backtests.RunAsync(effective.ToBacktestRequest(), cancellationToken);
                outcomes.Add(ComparisonOutcome.Success(strategy, effective, result));
            }
            catch (BacktestValidationException exception)
            {
                outcomes.Add(ComparisonOutcome.Failure(id, strategy.Name, exception.Message));
            }
            catch (MarketDataException exception)
            {
                return await FormAsync([], FormAlert.From(exception), cancellationToken);
            }
        }

        var permalink = $"{Request.Scheme}://{Request.Host}{ActionPath}?{query.ToQueryString()}";
        return View(CompareResultViewModel.Create(query, outcomes, permalink, PathBase));
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
        var model = CompareFormViewModel.Create(Request.Query, strategies, cachedSymbols, errors, alert);
        return View(nameof(Index), model);
    }
}
