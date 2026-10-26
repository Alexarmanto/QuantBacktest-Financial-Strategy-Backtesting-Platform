using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

internal sealed record SettingRow(string Label, string Value);

/// <summary>
/// Header of the result page (P4-U3): what was tested, with which settings, and the strategy card.
/// UnadjustedWarning is null when the prices are adjusted.
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
}
