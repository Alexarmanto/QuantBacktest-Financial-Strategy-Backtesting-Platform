using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One parameter an axis can sweep, with its catalog range and the P4-X2 default step. optimize-form.js copies these
/// values into the range inputs when the parameter of the axis changes.
/// </summary>
internal sealed record SweepAxisChoice(string Key, string Label, string Min, string Max, string DefaultStep, bool IsInteger)
{
    public static SweepAxisChoice From(ParameterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new SweepAxisChoice(
            definition.Key,
            definition.Label,
            BacktestQuery.FormatNumber(definition.Min),
            BacktestQuery.FormatNumber(definition.Max),
            BacktestQuery.FormatNumber(BacktestQuery.DefaultSweepStep(definition))!,
            definition.Type == ParameterType.Integer);
    }

    /// <summary>HTML step of the range inputs: whole numbers for an Integer parameter, free otherwise.</summary>
    public string HtmlStep => IsInteger ? NumberSetting.WholeStep : NumberSetting.AnyStep;
}

/// <summary>One number input of a range (min, max or step), with the value as typed (P4-U12).</summary>
internal sealed record SweepRangeInput(string Name, string Id, string Label, string Value, string? Min, string? Max, string Step, string? Error)
{
    /// <summary>Built with a TagBuilder, like NumberSetting: only the attributes that have a value are written.</summary>
    public IHtmlContent Input()
    {
        var input = new TagBuilder("input") { TagRenderMode = TagRenderMode.SelfClosing };
        input.AddCssClass(Error is null ? "form-control num" : "form-control num is-invalid");
        input.Attributes["type"] = "number";
        input.Attributes["id"] = Id;
        input.Attributes["name"] = Name;
        input.Attributes["value"] = Value;
        input.Attributes["step"] = Step;
        input.Attributes["required"] = "required";
        input.Attributes["data-range"] = Name;
        if (Min is not null)
        {
            input.Attributes["min"] = Min;
        }

        if (Max is not null)
        {
            input.Attributes["max"] = Max;
        }

        return input;
    }
}

/// <summary>
/// One axis of the heatmap form (P4-X2): the parameter select (x or y) and its min, max and step inputs.
/// Ids are prefixed by the strategy id, so the fieldsets of several strategies never share an id.
/// </summary>
internal sealed record SweepAxisField(
    string Axis,
    string Name,
    string Id,
    string SelectedKey,
    IReadOnlyList<SweepAxisChoice> Choices,
    string? Error,
    SweepRangeInput Min,
    SweepRangeInput Max,
    SweepRangeInput Step)
{
    public IReadOnlyList<SweepRangeInput> Ranges => [Min, Max, Step];

    /// <summary>The parameter select; every data-* value of an option is always set (built here, never by Razor).</summary>
    public IHtmlContent Select()
    {
        var select = new TagBuilder("select");
        select.AddCssClass(Error is null ? "form-select" : "form-select is-invalid");
        select.Attributes["id"] = Id;
        select.Attributes["name"] = Name;
        select.Attributes["data-axis"] = Name;
        foreach (var choice in Choices)
        {
            var option = new TagBuilder("option");
            option.Attributes["value"] = choice.Key;
            option.Attributes["data-min"] = choice.Min;
            option.Attributes["data-max"] = choice.Max;
            option.Attributes["data-default-step"] = choice.DefaultStep;
            option.Attributes["data-step"] = choice.HtmlStep;
            if (choice.Key == SelectedKey)
            {
                option.Attributes["selected"] = "selected";
            }

            option.InnerHtml.Append(choice.Label);
            select.InnerHtml.AppendHtml(option);
        }

        return select;
    }
}
