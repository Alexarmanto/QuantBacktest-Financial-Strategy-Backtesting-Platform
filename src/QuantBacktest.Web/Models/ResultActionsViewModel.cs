using System.Globalization;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// Action buttons of the result page (P4-U7). Every URL carries the canonical query string of BacktestQuery, so each
/// page receives exactly the settings of this result. OptimizeUrl is null below 2 strategy parameters (heatmap X, Y);
/// MonteCarloUrl is null below Conventions.MinTradesForMonteCarlo trades (MC1), and the button is then disabled.
/// The PDF, CSV and Optimize routes come with their own PR, which then enables the button.
/// </summary>
internal sealed record ResultActionsViewModel(
    string EditSettingsUrl,
    string PdfUrl,
    string TradesCsvUrl,
    string? MonteCarloUrl,
    string? OptimizeUrl)
{
    public const string ComingSoon = "Coming soon";

    public static readonly string MonteCarloNeedsTrades =
        $"Needs at least {Conventions.MinTradesForMonteCarlo.ToString(CultureInfo.InvariantCulture)} trades";

    public const int MinParametersToOptimize = 2;

    public const string EditSettingsPath = "/Backtest";
    public const string ResultPath = "/Backtest/Result";
    public const string PdfPath = "/Backtest/Report.pdf";
    public const string TradesCsvPath = "/Backtest/Trades.csv";
    public const string MonteCarloPath = "/Backtest/MonteCarlo";
    public const string OptimizePath = "/Optimize";

    /// <param name="tradeCount">BacktestResult.Trades.Count: the k of MC1.</param>
    /// <param name="pathBase">Request.PathBase of the current request ("" when the app is served at the root).</param>
    public static ResultActionsViewModel Create(BacktestQuery query, IStrategy strategy, int tradeCount, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(pathBase);

        var queryString = "?" + query.ToQueryString();
        string Url(string path) => pathBase + path + queryString;

        // The canonical Monte Carlo URL with the default settings: the button never goes through a redirect.
        var monteCarloUrl = tradeCount >= Conventions.MinTradesForMonteCarlo
            ? pathBase + MonteCarloPath + "?" + query.ToQueryString(new MonteCarloRequest())
            : null;

        return new ResultActionsViewModel(
            Url(EditSettingsPath),
            Url(PdfPath),
            Url(TradesCsvPath),
            monteCarloUrl,
            strategy.Parameters.Count >= MinParametersToOptimize ? Url(OptimizePath) : null);
    }
}
