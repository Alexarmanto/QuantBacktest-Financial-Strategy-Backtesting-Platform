using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using QuantBacktest.Core;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class NumberSettingTests
{
    private static readonly string[] RangeAttributes = ["min", "max", "data-exclusive-min", "required"];

    private static NumberSetting Setting(string key) => NumberSetting.All.Single(s => s.Key == key);

    private static string Percent(decimal fraction) => BacktestQuery.FormatNumber(fraction * 100m)!;

    private static string Render(NumberSetting setting, string value = "1", bool invalid = false)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        setting.Input(value, invalid).WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private static Dictionary<string, string> Attributes(string html) =>
        Regex.Matches(html, "([a-z-]+)=\"([^\"]*)\"").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    /// <summary>What the browser and backtest-form.js accept, from the rendered attributes only.</summary>
    private static bool Accepts(string html, string value)
    {
        var attributes = Attributes(html);
        if (value.Length == 0)
        {
            return !attributes.ContainsKey("required");
        }

        var number = decimal.Parse(value, CultureInfo.InvariantCulture);
        decimal Bound(string name) => decimal.Parse(attributes[name], CultureInfo.InvariantCulture);
        return (!attributes.ContainsKey("min") || number >= Bound("min"))
            && (!attributes.ContainsKey("max") || number <= Bound("max"))
            && (!attributes.ContainsKey("data-exclusive-min") || number > Bound("data-exclusive-min"));
    }

    public static TheoryData<string, string?, string?, string?, bool> Ranges() => new()
    {
        { BacktestQuery.CapitalKey, null, null, "0", true },
        { BacktestQuery.FeeKey, "0", Percent(Conventions.MaxFeeRate), null, false },
        { BacktestQuery.SlippageKey, "0", Percent(Conventions.MaxSlippageRate), null, false },
        { BacktestQuery.RiskFreeKey, "0", Percent((decimal)Conventions.MaxRiskFreeRate), null, false },
        { BacktestQuery.StopLossKey, null, Percent(Conventions.MaxStopLossFraction), "0", false },
        { BacktestQuery.TakeProfitKey, null, Percent(Conventions.MaxTakeProfitFraction), "0", false },
        { BacktestQuery.TrailingStopKey, null, Percent(Conventions.MaxTrailingStopFraction), "0", false },
    };

    [Theory] // P4-U1, docs/CONTRACT.md (validation of a request): the rendered range of each setting, maxima from Conventions
    [MemberData(nameof(Ranges))]
    public void Input_RendersTheRangeOfTheContract(string key, string? min, string? max, string? exclusiveMin, bool required)
    {
        var expected = new Dictionary<string, string>();
        foreach (var (name, value) in new[] { ("min", min), ("max", max), ("data-exclusive-min", exclusiveMin), ("required", required ? "required" : null) })
        {
            if (value is not null)
            {
                expected[name] = value;
            }
        }

        var attributes = Attributes(Render(Setting(key)));

        attributes.Where(a => RangeAttributes.Contains(a.Key)).Should().BeEquivalentTo(expected);
        attributes.Should().Contain(new Dictionary<string, string> { ["type"] = "number", ["step"] = "any", ["id"] = key, ["name"] = key });
    }

    [Fact] // P4-U1 (bug of PR #22): no attribute without a value, so no "Must be greater than ." with an empty bound
    public void Input_HasNoEmptyAttribute_ButTheValue()
    {
        foreach (var setting in NumberSetting.All)
        {
            Attributes(Render(setting, value: "")).Where(a => a.Key != "value" && a.Value.Length == 0)
                .Should().BeEmpty(setting.Key);
        }
    }

    [Theory] // P4-U1, docs/CONTRACT.md: [0, max] accepts 0; > 0 and ]0, max] refuse it; empty disables the protective exits only
    [InlineData(BacktestQuery.FeeKey, "0", true)]
    [InlineData(BacktestQuery.SlippageKey, "0", true)]
    [InlineData(BacktestQuery.RiskFreeKey, "0", true)]
    [InlineData(BacktestQuery.FeeKey, "5", true)]
    [InlineData(BacktestQuery.FeeKey, "5.01", false)]
    [InlineData(BacktestQuery.SlippageKey, "-0.1", false)]
    [InlineData(BacktestQuery.CapitalKey, "0", false)]
    [InlineData(BacktestQuery.CapitalKey, "0.01", true)]
    [InlineData(BacktestQuery.CapitalKey, "", false)]
    [InlineData(BacktestQuery.StopLossKey, "0", false)]
    [InlineData(BacktestQuery.StopLossKey, "50", true)]
    [InlineData(BacktestQuery.StopLossKey, "50.1", false)]
    [InlineData(BacktestQuery.TakeProfitKey, "0", false)]
    [InlineData(BacktestQuery.TakeProfitKey, "500", true)]
    [InlineData(BacktestQuery.TrailingStopKey, "0", false)]
    [InlineData(BacktestQuery.TrailingStopKey, "", true)]
    public void Input_AcceptsItsRangeOnly(string key, string value, bool accepted)
    {
        Accepts(Render(Setting(key)), value).Should().Be(accepted);
    }

    [Theory] // P4-U1 (bug of PR #22): the form accepts its own defaults (slippage 0, risk-free rate 0), built-in or custom strategy
    [InlineData("sma-crossover")]
    [InlineData("custom-dip")]
    public void Input_AcceptsTheDefaultsOfTheForm(string strategy)
    {
        var form = BacktestFormViewModel.Create(
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["strategy"] = strategy }),
            [new StubStrategy { Id = "sma-crossover" }, new StubStrategy { Id = "custom-dip", Category = Core.Models.StrategyCategory.Custom }],
            ["AAPL"],
            [],
            alert: null);

        form.Value(BacktestQuery.SlippageKey).Should().Be("0");
        foreach (var setting in NumberSetting.All)
        {
            Accepts(Render(setting, form.Value(setting.Key)), form.Value(setting.Key)).Should().BeTrue(setting.Key);
        }
    }

    [Fact] // P4-U1: every help text names its bounds, as rendered
    public void Help_NamesTheBounds()
    {
        NumberSetting.All.Select(s => s.Help).Should().Equal(
            "Greater than 0.",
            "0 to 5%, per fill.",
            "0 to 2%.",
            "0 to 20%.",
            "Greater than 0, up to 50%. Empty = off.",
            "Greater than 0, up to 500%. Empty = off.",
            "Greater than 0, up to 50%. Empty = off.");
    }

    [Fact] // P4-U12: the value is kept as typed (encoded) and a server error marks the field invalid
    public void Input_KeepsTheValue_AndShowsTheError()
    {
        var html = Render(Setting(BacktestQuery.FeeKey), value: "1\"x", invalid: true);

        html.Should().Contain("value=\"1&quot;x\"");
        Attributes(html)["class"].Should().Be("form-control num is-invalid");
        Attributes(Render(Setting(BacktestQuery.FeeKey)))["class"].Should().Be("form-control num");
    }
}
