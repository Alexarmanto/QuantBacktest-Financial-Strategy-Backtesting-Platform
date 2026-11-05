using System.Globalization;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

internal sealed record SettingRow(string Label, string Value);

/// <summary>
/// Header of the result page (P4-U3): what was tested, with which settings, and the strategy card.
/// UnadjustedWarning is null when the prices are adjusted. Shared by the Monte Carlo page (P4-X4).
/// </summary>
internal sealed record ResultHeaderViewModel(
    string Symbol,
    string StrategyName,
    string Timeframe,
    string Period,
    IReadOnlyList<SettingRow> Execution,
    IReadOnlyList<SettingRow> Parameters,
    string Permalink,
    string? UnadjustedWarning,
    StrategyDocumentation Documentation)
{
    public const string UnadjustedPricesWarning =
        "Prices are not adjusted for splits or dividends: long periods may be distorted.";

    /// <param name="permalink">Absolute URL of the page that shows this header, copied by "Copy link".</param>
    public static ResultHeaderViewModel Create(BacktestResult result, IStrategy strategy, string permalink)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(permalink);

        var request = result.Request;
        var execution = request.Execution;

        // Actual period: first and last bar of the tested period (BacktestResult.Bars).
        var period = result.Bars.Count == 0
            ? DisplayFormat.NotAvailable
            : $"{FormatDate(result.Bars[0].Date)} to {FormatDate(result.Bars[^1].Date)}";

        var settings = ExecutionRows(execution);

        // Values sent with the request; a key the request did not carry runs with its catalog default.
        var parameters = strategy.Parameters
            .Select(definition => new SettingRow(
                definition.Label,
                BacktestQuery.FormatNumber(request.Parameters.TryGetValue(definition.Key, out var value) ? value : definition.DefaultValue)))
            .ToList();

        return new ResultHeaderViewModel(
            request.Symbol,
            result.StrategyName,
            request.Timeframe.ToString(),
            period,
            settings,
            parameters,
            permalink,
            result.PricesAdjusted ? null : UnadjustedPricesWarning,
            strategy.Documentation);
    }

    /// <summary>The execution settings in display form (fractions shown as percentages, disabled exits as "Off").</summary>
    public static IReadOnlyList<SettingRow> ExecutionRows(ExecutionSettings execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return
        [
            new("Initial capital", DisplayFormat.Money(execution.InitialCapital)),
            new("Fee", DisplayFormat.Percent((double)execution.FeeRate)),
            new("Slippage", DisplayFormat.Percent((double)execution.SlippageRate)),
            new("Stop-loss", FormatOptional(execution.StopLossFraction)),
            new("Take-profit", FormatOptional(execution.TakeProfitFraction)),
            new("Trailing stop", FormatOptional(execution.TrailingStopFraction)),
            new("Risk-free rate", DisplayFormat.Percent(execution.RiskFreeRate)),
        ];
    }

    private static string FormatDate(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);

    private static string FormatOptional(decimal? fraction) =>
        fraction is { } value ? DisplayFormat.Percent((double)value) : BacktestResultViewModel.Disabled;
}
