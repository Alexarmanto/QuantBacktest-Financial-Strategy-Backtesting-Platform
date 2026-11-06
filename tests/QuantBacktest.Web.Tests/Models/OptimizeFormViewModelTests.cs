using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class OptimizeFormViewModelTests
{
    private static async Task<IReadOnlyList<IStrategy>> FakeStrategies() =>
        await new FakeStrategyCatalog(new FakeCustomStrategyStore(), new FakeCustomStrategyFactory()).GetAllAsync();

    private static async Task<OptimizeFormViewModel> Form(string queryString, IReadOnlyList<QueryFieldError>? errors = null) =>
        OptimizeFormViewModel.Create(
            new QueryCollection(QueryHelpers.ParseQuery(queryString)),
            await FakeStrategies(),
            ["AAPL", "MSFT"],
            errors ?? [],
            alert: null);

    private static string Html(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        return writer.ToString();
    }

    [Fact] // P4-X2, O1: every catalog strategy is listed; one with fewer than 2 parameters is disabled, with the reason
    public async Task Form_ListsCatalog_DisablesFewerThanTwoParameters()
    {
        var form = await Form("");

        form.BuiltInStrategies.Select(o => o.Id).Should().Equal("fake-alternating", "fake-threshold");
        var custom = form.CustomStrategies.Should().ContainSingle().Subject;
        custom.Id.Should().Be("custom-trend-dip");
        custom.Ineligible.Should().Be(StrategyChecks.TooFewParametersRestriction);
        custom.Label.Should().EndWith("(fewer than 2 parameters)");
        custom.X.Should().BeNull();
        custom.IsSelected.Should().BeFalse();
    }

    [Fact] // P4-X2: blank form → the first strategy that can be optimised, axes = its first two parameters, P4-X2 defaults
    public async Task Form_Blank_DefaultAxesAndRanges()
    {
        var selected = (await Form("")).Selected!;

        selected.Id.Should().Be("fake-alternating");
        selected.X!.SelectedKey.Should().Be("period");
        selected.X.Ranges.Select(r => r.Value).Should().Equal("2", "200", "9");
        selected.Y!.SelectedKey.Should().Be("offset");
        selected.Y.Ranges.Select(r => r.Value).Should().Equal("0", "50", "3");
        selected.Parameters.Should().OnlyContain(p => p.IsSwept);
    }

    [Fact] // PART-4 section 7 (form): a daily-only strategy is marked in weekly; the default goes to one that supports it
    public async Task Form_Weekly_MarksDailyOnly_AndSelectsASupportedStrategy()
    {
        var form = await Form("tf=weekly");

        form.BuiltInStrategies[0].Restriction.Should().Be("daily only");
        form.Selected!.Id.Should().Be("fake-threshold");
    }

    [Fact] // P4-U7: the "Optimize this strategy" URL of the result page opens this form prefilled
    public async Task Form_PrefillsFromResultOptimizeUrl()
    {
        var strategies = await FakeStrategies();
        var threshold = strategies.Single(s => s.Id == "fake-threshold");
        BacktestQuery.TryParse(
            new QueryCollection(QueryHelpers.ParseQuery("symbol=MSFT&tf=weekly&strategy=fake-threshold&capital=5000&fee=0.2&slip=0&rf=1&p.lookback=12&p.threshold=0.03")),
            out var backtest,
            out _).Should().BeTrue();
        var url = ResultActionsViewModel.Create(backtest!, threshold, tradeCount: 0, pathBase: "").OptimizeUrl!;

        var form = await Form(url[(url.IndexOf('?') + 1)..]);

        form.Value("symbol").Should().Be("MSFT");
        form.Value("tf").Should().Be("weekly");
        form.Value("capital").Should().Be("5000");
        form.Value("fee").Should().Be("0.2");
        form.Value("rf").Should().Be("1");
        form.Selected!.Id.Should().Be("fake-threshold");
        form.Selected.X!.SelectedKey.Should().Be("lookback");
        form.Selected.Y!.SelectedKey.Should().Be("threshold");
        form.Selected.Parameters.Select(p => p.Field.Value).Should().Equal("12", "0.03");
        form.Metrics.Single(m => m.IsSelected).Token.Should().Be("sharpe");
    }

    [Fact] // P4-U12: the input is kept as typed and each error is shown under its field
    public async Task Form_KeepsInputAndShowsFieldErrors()
    {
        var form = await Form(
            "symbol=AAPL&strategy=fake-threshold&x=threshold&xmin=0.5&xmax=0.1&xstep=0.05&y=lookback&metric=calmar",
            [new QueryFieldError("xmin", "X minimum must not be greater than X maximum."), new QueryFieldError("cells", "Too many.")]);

        var x = form.Selected!.X!;
        x.SelectedKey.Should().Be("threshold");
        x.Min.Value.Should().Be("0.5");
        x.Min.Error.Should().Be("X minimum must not be greater than X maximum.");
        form.Selected.Y!.SelectedKey.Should().Be("lookback");
        form.OtherErrors.Should().Equal("Too many.");
        form.Metrics.Single(m => m.IsSelected).Token.Should().Be("calmar");
    }

    [Fact] // P4-X2: the axis select carries every parameter with its range and default step; no empty data-* attribute
    public async Task Form_SweepInputs_NoEmptyDataAttribute()
    {
        var x = (await Form("strategy=fake-threshold")).Selected!.X!;

        var select = Html(x.Select());
        select.Should().Contain("id=\"fake-threshold-x\"").And.Contain("name=\"x\"");
        select.Should().Contain("data-max=\"0.5\"").And.Contain("data-max=\"100\"");
        select.Should().Contain("data-default-step=\"0.025\"").And.Contain("data-default-step=\"5\"");
        select.Should().NotContain("=\"\"");
        var step = Html(x.Step.Input());
        step.Should().Contain("name=\"xstep\"").And.NotContain("min=").And.NotContain("=\"\"");
        Html(x.Min.Input()).Should().Contain("min=\"1\"").And.Contain("max=\"100\"").And.Contain("step=\"1\"");
    }

    [Fact] // P4-X2: the other parameters of a 3-parameter strategy stay editable, the swept ones are not sent
    public async Task Form_OtherParameters_SweptOnesDisabled()
    {
        var form = OptimizeFormViewModel.Create(
            new QueryCollection(QueryHelpers.ParseQuery("strategy=rsi-reversion&p.overbought=75")),
            [BacktestQueryOptimizationTests.Rsi],
            [],
            [],
            alert: null);

        var selected = form.Selected!;
        selected.HasOtherParameters.Should().BeTrue();
        selected.Parameters.Select(p => (p.Key, p.IsSwept, p.Field.Value)).Should().Equal(
            ("period", true, "14"), ("oversold", true, "30"), ("overbought", false, "75"));
    }
}
