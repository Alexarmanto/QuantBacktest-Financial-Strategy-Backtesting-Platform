using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Backtest form, permalink results and downloads (PART-4 section 5, P4-U1, P4-U12). Thin: BacktestQuery reads and
/// writes the URL, the view models format, IBacktestService computes, IReportGenerator builds the files. Nothing
/// financial happens here.
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
                return View(MonteCarloInvalidSettings(strategy, effective, monteCarloErrors));
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
            return View(MonteCarloRefused(result, strategy, effective, monteCarloRequest!, exception, permalink));
        }
    }

    /// <summary>
    /// GET /Backtest/Report.pdf?… (PART-4 section 5): the PDF of IReportGenerator (Part 1), served as is. With
    /// &amp;mc=true&amp;sims=&amp;seed=, IMonteCarloService runs first and the PDF gets its Monte Carlo section.
    /// Same reading, canonical redirect and errors as Result: invalid backtest settings, an mc other than "true" or a
    /// refused backtest → the backtest form. With mc=true, invalid sims / seed (nothing run) or a refused simulation
    /// (MC1) → the Monte Carlo page, built by the same code as GET /Backtest/MonteCarlo (decisions D1 to D4).
    /// The generator declares no exception: anything it throws is a bug and is not caught.
    /// </summary>
    [HttpGet(ResultActionsViewModel.PdfPath)]
    public async Task<IActionResult> Report(
        [FromServices] IBacktestService backtests,
        [FromServices] IMonteCarloService monteCarlo,
        [FromServices] IReportGenerator reports,
        CancellationToken cancellationToken)
    {
        if (Request.Query.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        var backtestValid = BacktestQuery.TryParse(Request.Query, out var query, out var errors);
        var flagValid = BacktestQuery.TryParseReportFlag(Request.Query, out var withMonteCarlo, out var flagErrors);
        if (!backtestValid || !flagValid)
        {
            return await FormAsync([.. errors, .. flagErrors], alert: null, cancellationToken);
        }

        // sims and seed only mean something with mc=true; without it they are dropped by the canonical redirect.
        MonteCarloRequest? monteCarloRequest = null;
        IReadOnlyList<QueryFieldError> monteCarloErrors = [];
        var monteCarloValid = !withMonteCarlo
            || BacktestQuery.TryParseMonteCarlo(Request.Query, out monteCarloRequest, out monteCarloErrors);

        BacktestResult result;
        IStrategy strategy;
        BacktestQuery effective;
        try
        {
            var (found, strategyErrors) = await ResolveStrategyAsync(query!, cancellationToken);
            if (found is null)
            {
                return await FormAsync(strategyErrors, alert: null, cancellationToken);
            }

            strategy = found;
            effective = query!.WithDefaultParameters(strategy.Parameters);
            if (!monteCarloValid)
            {
                return View(nameof(MonteCarlo), MonteCarloInvalidSettings(strategy, effective, monteCarloErrors));
            }

            if (monteCarloRequest is null)
            {
                if (!effective.IsCanonical(Request.QueryString))
                {
                    return Redirect(ActionPath + "?" + effective.ToQueryString());
                }
            }
            else if (!effective.IsCanonical(Request.QueryString, monteCarloRequest, forReport: true))
            {
                return Redirect(ActionPath + "?" + effective.ToQueryString(monteCarloRequest, forReport: true));
            }

            result = await backtests.RunAsync(effective.ToBacktestRequest(), cancellationToken);
        }
        catch (BacktestValidationException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }
        catch (MarketDataException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }

        MonteCarloResult? simulation = null;
        if (monteCarloRequest is not null)
        {
            try
            {
                simulation = monteCarlo.Run(result, monteCarloRequest);
            }
            catch (BacktestValidationException exception)
            {
                // MC1: the page of the simulation explains the refusal; its Copy link is the Monte Carlo page, not the PDF.
                var permalink = AbsoluteUrl(PathBase + ResultActionsViewModel.MonteCarloPath, effective.ToQueryString(monteCarloRequest));
                return View(nameof(MonteCarlo), MonteCarloRefused(result, strategy, effective, monteCarloRequest, exception, permalink));
            }
        }

        var pdf = reports.GeneratePdf(result, simulation);
        return File(pdf, DownloadFile.PdfContentType, DownloadFile.PdfFileName(result));
    }

    /// <summary>
    /// GET /Backtest/Trades.csv?… (PART-4 section 5): the trade log of IReportGenerator.GenerateTradesCsv (format:
    /// docs/CONTRACT.md section 11), UTF-8 without BOM, Content-Type exactly text/csv. Same reading, canonical redirect
    /// and errors as Result; mc, sims and seed are not part of this URL and are dropped by the redirect.
    /// </summary>
    [HttpGet(ResultActionsViewModel.TradesCsvPath)]
    public async Task<IActionResult> TradesCsv(
        [FromServices] IBacktestService backtests,
        [FromServices] IReportGenerator reports,
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

        BacktestResult result;
        try
        {
            var (strategy, strategyErrors) = await ResolveStrategyAsync(query, cancellationToken);
            if (strategy is null)
            {
                return await FormAsync(strategyErrors, alert: null, cancellationToken);
            }

            var effective = query.WithDefaultParameters(strategy.Parameters);
            if (!effective.IsCanonical(Request.QueryString))
            {
                return Redirect(ActionPath + "?" + effective.ToQueryString());
            }

            result = await backtests.RunAsync(effective.ToBacktestRequest(), cancellationToken);
        }
        catch (BacktestValidationException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }
        catch (MarketDataException exception)
        {
            return await FormAsync([], FormAlert.From(exception), cancellationToken);
        }

        var csv = reports.GenerateTradesCsv(result);
        return File(DownloadFile.CsvEncoding.GetBytes(csv), DownloadFile.CsvContentType, DownloadFile.CsvFileName(result));
    }

    /// <summary>
    /// The Monte Carlo page when sims or seed is invalid. Shared by MonteCarlo and Report (decision D2): its form
    /// always submits to /Backtest/MonteCarlo (MonteCarloViewModel builds Form.Action), never to the current URL.
    /// </summary>
    private MonteCarloViewModel MonteCarloInvalidSettings(
        IStrategy strategy,
        BacktestQuery effective,
        IReadOnlyList<QueryFieldError> errors) =>
        MonteCarloViewModel.InvalidSettings(strategy, effective, Request.Query, errors, PathBase);

    /// <summary>
    /// The Monte Carlo page when IMonteCarloService refused the simulation (MC1). Shared by MonteCarlo and Report
    /// (decision D3). The permalink is given by the caller, so MonteCarlo keeps its own (built from the received path).
    /// </summary>
    private MonteCarloViewModel MonteCarloRefused(
        BacktestResult result,
        IStrategy strategy,
        BacktestQuery effective,
        MonteCarloRequest request,
        BacktestValidationException exception,
        string permalink) =>
        MonteCarloViewModel.Refused(result, strategy, effective, request, exception, permalink, PathBase);

    private string AbsoluteUrl(string path, string queryString) => $"{Request.Scheme}://{Request.Host}{path}?{queryString}";

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
            return (null, [new QueryFieldError(BacktestQuery.StrategyKey, StrategyChecks.UnknownStrategy(query.StrategyId))]);
        }

        var strategyErrors = query.ValidateParameters(strategy.Parameters).ToList();
        if (StrategyChecks.TimeframeError(strategy, query.Timeframe) is { } timeframeError)
        {
            strategyErrors.Insert(0, new QueryFieldError(BacktestQuery.StrategyKey, timeframeError));
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
        var cachedSymbols = await CachedSymbols.GetAsync(marketData, logger, cancellationToken);
        var model = BacktestFormViewModel.Create(Request.Query, strategies, cachedSymbols, errors, alert);
        return View(nameof(Index), model);
    }
}
