using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Web.Models;

/// <summary>
/// Action buttons of the result page (P4-U7). Every URL carries the canonical query string of BacktestQuery, so each
/// page receives exactly the settings of this result. OptimizeUrl is null below 2 strategy parameters (heatmap X, Y).
/// Only Edit settings works today: the other routes come with their own PR, which then enables the button.
/// </summary>
internal sealed record ResultActionsViewModel(
    string EditSettingsUrl,
    string PdfUrl,
    string TradesCsvUrl,
    string MonteCarloUrl,
    string? OptimizeUrl)
{
    public const string ComingSoon = "Coming soon";

    public const int MinParametersToOptimize = 2;

    public const string EditSettingsPath = "/Backtest";
    public const string PdfPath = "/Backtest/Report.pdf";
    public const string TradesCsvPath = "/Backtest/Trades.csv";
    public const string MonteCarloPath = "/Backtest/MonteCarlo";
    public const string OptimizePath = "/Optimize";

    /// <param name="pathBase">Request.PathBase of the current request ("" when the app is served at the root).</param>
    public static ResultActionsViewModel Create(BacktestQuery query, IStrategy strategy, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(pathBase);

        var queryString = "?" + query.ToQueryString();
        string Url(string path) => pathBase + path + queryString;

        return new ResultActionsViewModel(
            Url(EditSettingsPath),
            Url(PdfPath),
            Url(TradesCsvPath),
            Url(MonteCarloPath),
            strategy.Parameters.Count >= MinParametersToOptimize ? Url(OptimizePath) : null);
    }
}
