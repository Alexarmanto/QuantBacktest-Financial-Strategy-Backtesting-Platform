using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Data and quality pages (P4-U10, P4-U11; PART-4 section 5 routes). Thin: BacktestQuery reads and writes the URLs,
/// the view models format. Everything comes from IMarketDataService; the two GET pages never call GetSeriesAsync,
/// so looking at them costs no API call. Only POST /Data/Refresh asks the API, and always answers with a redirect
/// (post/redirect/get): reloading the next page never sends the request again.
/// </summary>
public sealed class DataController(
    IMarketDataService marketData,
    ILogger<DataController> logger) : Controller
{
    /// <summary>
    /// GET /Data: in this order, GetCachedSymbolsAsync; for each symbol (service order), GetQualityReportAsync daily
    /// then weekly, one after the other (singleton service on SQLite); then GetApiQuotaAsync once. NotCached gives a
    /// "not cached" row; nothing else is caught. The refresh form is prefilled from symbol and tf (tf absent: daily;
    /// unknown keys ignored; no redirect) and shows what the redirect of POST /Data/Refresh brought back.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var symbols = await marketData.GetCachedSymbolsAsync(cancellationToken);
        var entries = new List<DataSeriesEntry>();
        foreach (var symbol in symbols)
        {
            foreach (var timeframe in DataLabels.Timeframes)
            {
                entries.Add(new DataSeriesEntry(symbol, timeframe, await ReportIfCachedAsync(symbol, timeframe, cancellationToken)));
            }
        }

        var quota = await marketData.GetApiQuotaAsync(cancellationToken);
        var (prefillSymbol, prefillTimeframe) = BacktestQuery.ReadRefreshPrefill(Request.Query);
        var form = DataRefreshFormViewModel.ForInput(
            prefillSymbol,
            prefillTimeframe,
            symbols,
            DataTempData.ReadFieldErrors(TempData),
            DataTempData.ReadAlert(TempData),
            quota.Remaining);
        return View(DataIndexViewModel.Create(entries, quota, form, PathBase));
    }

    /// <summary>
    /// GET /Data/Quality?symbol=&amp;tf=: symbol required, tf daily by default. An invalid link shows its errors and
    /// calls no service. A valid link that is not canonical (lower-case symbol, tf missing or in another case, unknown
    /// keys, other order) is redirected once. Then GetQualityReportAsync: a MarketDataException (NotCached, in
    /// practice) is shown as an alert with the refresh form, status 200; nothing else is caught. GetApiQuotaAsync
    /// gives the number of the confirmation of the refresh form.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Quality(CancellationToken cancellationToken)
    {
        if (!BacktestQuery.TryParseDataSeries(Request.Query, out var series, out var errors))
        {
            return View(DataQualityViewModel.Invalid(errors, PathBase));
        }

        if (!BacktestQuery.IsCanonicalData(Request.QueryString, series))
        {
            return Redirect($"{Request.PathBase}{Request.Path}?{series.ToQueryString()}");
        }

        var notice = PageNotice.ReadFrom(TempData);
        DataQualityReport? report = null;
        FormAlert? alert = null;
        try
        {
            report = await marketData.GetQualityReportAsync(series.Symbol, series.Timeframe, cancellationToken);
        }
        catch (MarketDataException exception)
        {
            alert = FormAlert.ForRefresh(exception);
        }

        var quota = await marketData.GetApiQuotaAsync(cancellationToken);
        var form = DataRefreshFormViewModel.ForSeries(series, quota.Remaining);
        return View(report is null
            ? DataQualityViewModel.Unavailable(series, alert!, notice, form, PathBase)
            : DataQualityViewModel.ForReport(series, report, notice, form, PathBase));
    }

    /// <summary>
    /// POST /Data/Refresh (P4-U10): reads symbol and tf of the form like a URL (BacktestQuery). Invalid input: no
    /// service call, back to the prefilled form of /Data with the field errors. Valid: exactly one
    /// GetSeriesAsync(forceRefresh: true); success → the canonical quality page with a message; MarketDataException →
    /// the form of /Data with the alert of P4-U12. Every answer is a redirect; nothing else is caught.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(IFormCollection form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!BacktestQuery.TryParseRefresh(form, out var series, out var errors))
        {
            DataTempData.WriteFieldErrors(TempData, errors);
            var prefill = BacktestQuery.ToRefreshPrefillQueryString(form);
            return Redirect(PathBase + DataRoutes.IndexPath + (prefill.Length == 0 ? string.Empty : "?" + prefill) + DataRoutes.RefreshAnchor);
        }

        PriceSeries refreshed;
        try
        {
            refreshed = await marketData.GetSeriesAsync(series.Symbol, series.Timeframe, forceRefresh: true, cancellationToken);
        }
        catch (MarketDataException exception)
        {
            logger.LogWarning("Refresh of {Symbol} {Timeframe} refused: {Kind}.", series.Symbol, series.Timeframe, exception.Kind);
            DataTempData.WriteAlert(TempData, FormAlert.ForRefresh(exception));
            return Redirect(series.RefreshFormUrl(PathBase));
        }

        logger.LogInformation(
            "Refreshed {Symbol} {Timeframe}: {BarCount} bars.", series.Symbol, series.Timeframe, refreshed.Bars.Count);
        PageNotice.Success(RefreshedMessage(series, refreshed.Bars.Count)).WriteTo(TempData);
        return Redirect(series.QualityUrl(PathBase));
    }

    /// <summary>"JPM (weekly) refreshed from Alpha Vantage: 1,406 bars in the cache." The count is the service's.</summary>
    internal static string RefreshedMessage(DataSeriesQuery series, int barCount)
    {
        ArgumentNullException.ThrowIfNull(series);
        return $"{series.Symbol} ({BacktestQuery.FormatTimeframe(series.Timeframe)}) refreshed from Alpha Vantage: "
            + $"{Infrastructure.DisplayFormat.Integer(barCount)} bars in the cache.";
    }

    private string PathBase => Request.PathBase.Value ?? string.Empty;

    /// <summary>The quality report, or null when the series is not cached (the only error a row absorbs).</summary>
    private async Task<DataQualityReport?> ReportIfCachedAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken)
    {
        try
        {
            return await marketData.GetQualityReportAsync(symbol, timeframe, cancellationToken);
        }
        catch (MarketDataException exception) when (exception.Kind == MarketDataErrorKind.NotCached)
        {
            return null;
        }
    }
}
