using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

public class ValidateFormAndLinkTests
{
    private const string OptimizeCanonical = "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0"
        + "&x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025&metric=sharpe";

    private static readonly IReadOnlyList<IStrategy> Strategies = [BacktestQueryOptimizationTests.Threshold];

    private static QueryCollection Query(string url) => new(QueryHelpers.ParseQuery(url));

    private static ValidateFormViewModel Form(string url, IReadOnlyList<QueryFieldError>? errors = null) =>
        ValidateFormViewModel.Create(Query(url), Strategies, ["AAPL"], errors ?? [], alert: null);

    // ---- Form ----

    [Fact] // P4-X3: train in percent, 50 to 90, default 70 (Conventions.DefaultTrainFraction), required, any decimal
    public void Form_TrainField_DefaultAndBounds()
    {
        var form = Form("strategy=fake-threshold");

        form.TrainValue.Should().Be("70");
        form.TrainError.Should().BeNull();
        ValidateFormViewModel.Train.Should().BeEquivalentTo(new
        {
            Key = "train",
            Min = "50",
            Max = "90",
            ExclusiveMin = (string?)null,
            Required = true,
            Step = NumberSetting.AnyStep,
        });
        ValidateFormViewModel.MaxCells.Should().Be(625);
    }

    [Fact] // P4-X3: the form is the heatmap form, prefilled from the URL (the link of the heatmap note)
    public void Form_HeatmapFieldsPrefilled_SameAsOptimizeForm()
    {
        var url = OptimizeCanonical + "&train=80";
        var form = Form(url);
        var optimize = OptimizeFormViewModel.Create(Query(url), Strategies, ["AAPL"], [], null);

        form.TrainValue.Should().Be("80");
        form.Sweep.Selected!.Id.Should().Be("fake-threshold");
        form.Sweep.Selected.X!.SelectedKey.Should().Be(optimize.Selected!.X!.SelectedKey);
        form.Sweep.Value("symbol").Should().Be("AAPL");
        form.Sweep.Metrics.Should().Equal(optimize.Metrics);
    }

    [Fact] // P4-U12, decision 7: a field error of train stays under its input, the input is kept as typed
    public void Form_TrainError_OnTheField_InputKept()
    {
        var form = Form(OptimizeCanonical + "&train=95", [new QueryFieldError("train", BacktestQuery.TrainRangeMessage)]);

        form.TrainValue.Should().Be("95");
        form.TrainError.Should().Be(BacktestQuery.TrainRangeMessage);
        form.Sweep.HasErrors.Should().BeTrue();
        form.Sweep.OtherErrors.Should().BeEmpty();
    }

    [Fact] // the heatmap form without extra fields keeps an unknown "train" error at the top, as before this PR
    public void OptimizeForm_WithoutExtraFields_Unchanged()
    {
        var optimize = OptimizeFormViewModel.Create(Query(OptimizeCanonical), Strategies, ["AAPL"], [new QueryFieldError("train", "x")], null);

        optimize.OtherErrors.Should().Equal("x");
        optimize.Error("train").Should().BeNull();
    }

    // ---- Link of the heatmap note ----

    [Fact] // P4-X2 note, P4-X3: the note text is unchanged; "Validate page" is its link
    public void OptimisticNote_PartsMakeTheSameText()
    {
        (HeatmapViewModel.OptimisticNoteStart + HeatmapViewModel.OptimisticNoteLink + HeatmapViewModel.OptimisticNoteEnd)
            .Should().Be(HeatmapViewModel.OptimisticNote)
            .And.Be("The best cell is optimistic by construction: validate it on unseen data (Validate page).");
        HeatmapViewModel.OptimisticNoteLink.Should().Be("Validate page");
    }

    [Fact] // P4-X3: the note links to /Validate prefilled with the same grid and period, train=70, canonical
    public void OptimizeResult_ValidateUrl_SameGrid_Canonical()
    {
        var url = OptimizeCanonical.Replace("&capital", "&from=2018-03-01&to=2019-01-31&capital", StringComparison.Ordinal);
        BacktestQuery.TryParseOptimization(Query(url), out var parsed, out _).Should().BeTrue();
        parsed!.Resolve(BacktestQueryOptimizationTests.Threshold, out var query).Should().BeEmpty();
        var result = new SweepResult(query!.ToSweepRequest(), [1], [0], [new SweepCell(1, 0, 0.1, 10, null)], null, null, false);

        var model = OptimizeResultViewModel.Create(result, BacktestQueryOptimizationTests.Threshold, query, "p", "/app");

        model.ValidateUrl.Should().Be("/app/Validate?" + url + "&train=70");
        var validate = model.ValidateUrl["/app/Validate?".Length..];
        BacktestQuery.TryParseValidation(Query(validate), out var validation, out _).Should().BeTrue();
        validation!.Resolve(BacktestQueryOptimizationTests.Threshold, out var effective).Should().BeEmpty();
        effective!.IsCanonical(new QueryString("?" + validate)).Should().BeTrue();
    }

    [Fact] // P4-X2 unchanged: the heatmap page links its cells; Payload is what Json serialises
    public void HeatmapPage_CellsLinked_PayloadIsTheJson()
    {
        BacktestQuery.TryParseOptimization(Query(OptimizeCanonical), out var parsed, out _).Should().BeTrue();
        parsed!.Resolve(BacktestQueryOptimizationTests.Threshold, out var query).Should().BeEmpty();
        var result = new SweepResult(query!.ToSweepRequest(), [1], [0], [new SweepCell(1, 0, 0.1, 10, null)], null, null, false);

        var heatmap = HeatmapViewModel.Create(result, BacktestQueryOptimizationTests.Threshold, query, "");

        heatmap.CellsLinked.Should().BeTrue();
        JsonNode.DeepEquals(
                JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(heatmap.Payload, ResultChartsViewModel.JsonOptions)),
                JsonNode.Parse(heatmap.Json))
            .Should().BeTrue();
    }
}
