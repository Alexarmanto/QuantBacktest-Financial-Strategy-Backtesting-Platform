using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Train/test validation (P4-X3, docs/CONTRACT.md T1 to T3; "Partie 4 orchestre"). Thin: BacktestQuery reads and
/// writes the URL, TrainTestSplit cuts the bars (T1, T2), the view models format. The services compute everything:
/// IParameterSweepService the training heatmap and its best cell (O3), IBacktestService the two backtests.
/// </summary>
public sealed class ValidateController(
    IStrategyCatalog catalog,
    IMarketDataService marketData,
    ILogger<ValidateController> logger) : Controller
{
    /// <summary>GET /Validate: the form, prefilled from the URL parameters present (the link of the heatmap note). Never runs anything.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        await FormAsync([], alert: null, cancellationToken);

    /// <summary>
    /// GET /Validate/Result?…: validates the URL, redirects once to its canonical form, then, in this order:
    /// (1) IMarketDataService.GetSeriesAsync without refresh (P4-X3 "série via GetSeriesAsync (sans rafraîchissement)");
    /// (2) the T1 split of the bars of [from, to], refused at the top of the form when the period is empty or a part
    /// has fewer than 2 bars (nothing else is called); (3) IParameterSweepService.RunAsync on the training period;
    /// (4) only with a best cell: IBacktestService.RunAsync on the training period, then on the test period, with the
    /// best values and the same settings. Backtest (4a) is not in T3 ("un seul backtest sur le test"): see
    /// ValidationQuery.TrainBacktest. Form errors, nothing run: as on /Optimize, plus the training share.
    /// BacktestValidationException and MarketDataException (at any step, the first one stops): the form with the
    /// alert of P4-U12. Nothing else is caught: any other exception is a bug (/Home/Error).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Result(
        [FromServices] IParameterSweepService sweeps,
        [FromServices] IBacktestService backtests,
        CancellationToken cancellationToken)
    {
        if (Request.Query.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!BacktestQuery.TryParseValidation(Request.Query, out var query, out var errors))
        {
            return await FormAsync(errors, alert: null, cancellationToken);
        }

        IStrategy? strategy;
        ValidationQuery? effective;
        TrainTestSplit? split;
        SweepResult sweep;
        BacktestResult? train = null;
        BacktestResult? test = null;
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

            var series = await marketData.GetSeriesAsync(effective.Symbol, effective.Timeframe, false, cancellationToken);
            var settings = effective.Settings;
            if (!TrainTestSplit.TryCreate(series, settings.From, settings.To, effective.TrainPercent, out split, out var splitError))
            {
                // Depends on from, to and train together, and is known only once the bars are loaded: a general alert.
                return await FormAsync([], new FormAlert(splitError), cancellationToken);
            }

            sweep = await sweeps.RunAsync(effective.TrainGrid(split).ToSweepRequest(), cancellationToken);
            if (sweep.Best is { } best)
            {
                train = await backtests.RunAsync(effective.TrainBacktest(split, best).ToBacktestRequest(), cancellationToken);
                test = await backtests.RunAsync(effective.TestBacktest(split, best).ToBacktestRequest(), cancellationToken);
            }
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
        return View(ValidateResultViewModel.Create(sweep, train, test, strategy, effective, split, permalink, PathBase));
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
        var model = ValidateFormViewModel.Create(Request.Query, strategies, cachedSymbols, errors, alert);
        return View(nameof(Index), model);
    }
}
