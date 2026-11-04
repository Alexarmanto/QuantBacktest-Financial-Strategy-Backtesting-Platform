using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One execution setting of the backtest form (P4-U1) in URL units (percent), with the range of the contract
/// (docs/CONTRACT.md, validation of a request); the maxima come from Conventions through BacktestQuery.
/// Min is the inclusive HTML min. ExclusiveMin is a ]min, max] range, which HTML cannot express: backtest-form.js
/// checks it and names the bound in its message.
/// </summary>
/// <remarks>
/// The input is built here, not in Razor: Razor always writes a data-* attribute, even when its value is null
/// (data-exclusive-min="" on the closed ranges, read as 0 by the script, so 0 was refused with an empty bound).
/// A TagBuilder only writes the attributes that have a value.
/// </remarks>
internal sealed record NumberSetting(
    string Key,
    string Label,
    string? Min,
    string? Max,
    string? ExclusiveMin,
    bool Required,
    string Help,
    string Step = NumberSetting.AnyStep)
{
    /// <summary>HTML step of a free decimal; a whole-number setting (Monte Carlo sims, seed) uses "1".</summary>
    public const string AnyStep = "any";
    public const string WholeStep = "1";

    private static readonly string Zero = BacktestQuery.FormatNumber(0m)!;

    /// <summary>Capital, fee, slippage, risk-free rate, then the optional protective exits, in display order.</summary>
    public static IReadOnlyList<NumberSetting> All { get; } =
    [
        new(BacktestQuery.CapitalKey, "Initial capital ($)", null, null, Zero, true, $"Greater than {Zero}."),
        Closed(BacktestQuery.FeeKey, "Fee (%)", BacktestQuery.MaxFeePercent, ", per fill."),
        Closed(BacktestQuery.SlippageKey, "Slippage (%)", BacktestQuery.MaxSlippagePercent, "."),
        Closed(BacktestQuery.RiskFreeKey, "Risk-free rate (%/year)", BacktestQuery.MaxRiskFreePercent, "."),
        Optional(BacktestQuery.StopLossKey, "Stop-loss (%)", BacktestQuery.MaxStopLossPercent),
        Optional(BacktestQuery.TakeProfitKey, "Take-profit (%)", BacktestQuery.MaxTakeProfitPercent),
        Optional(BacktestQuery.TrailingStopKey, "Trailing stop (%)", BacktestQuery.MaxTrailingStopPercent),
    ];

    /// <summary>The input of the setting, with the value as typed (P4-U12) and the error state of the server.</summary>
    public IHtmlContent Input(string value, bool invalid)
    {
        var input = new TagBuilder("input") { TagRenderMode = TagRenderMode.SelfClosing };
        input.AddCssClass(invalid ? "form-control num is-invalid" : "form-control num");
        input.Attributes["type"] = "number";
        input.Attributes["step"] = Step;
        input.Attributes["id"] = Key;
        input.Attributes["name"] = Key;
        input.Attributes["value"] = value;
        SetIfPresent(input, "min", Min);
        SetIfPresent(input, "max", Max);
        SetIfPresent(input, "data-exclusive-min", ExclusiveMin);
        if (Required)
        {
            input.Attributes["required"] = "required";
        }

        return input;
    }

    /// <summary>[0, max]: 0 is accepted.</summary>
    private static NumberSetting Closed(string key, string label, decimal max, string helpEnd)
    {
        var maximum = BacktestQuery.FormatNumber(max)!;
        return new(key, label, Zero, maximum, null, false, $"{Zero} to {maximum}%{helpEnd}");
    }

    /// <summary>]0, max]: 0 is refused, empty means disabled.</summary>
    private static NumberSetting Optional(string key, string label, decimal max)
    {
        var maximum = BacktestQuery.FormatNumber(max)!;
        return new(key, label, null, maximum, Zero, false, $"Greater than {Zero}, up to {maximum}%. Empty = off.");
    }

    private static void SetIfPresent(TagBuilder tag, string name, string? value)
    {
        if (value is not null)
        {
            tag.Attributes[name] = value;
        }
    }
}
