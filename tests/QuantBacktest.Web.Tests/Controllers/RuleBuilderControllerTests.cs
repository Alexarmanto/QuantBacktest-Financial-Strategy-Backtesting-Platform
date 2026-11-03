using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Controllers;

/// <summary>GET /Strategies/Custom/New, GET /Strategies/Custom/{id}/Edit and POST /Strategies/Custom/Save (P4-U9).</summary>
public class RuleBuilderControllerTests
{
    private const string ExampleId = "custom-trend-dip";

    /// <summary>The fakes of the app; the factory adds the given problems, as the real Validate would.</summary>
    private sealed class Fixture
    {
        public Fixture(IReadOnlyList<string>? problems = null)
        {
            Store = new RecordingCustomStrategyStore(new FakeCustomStrategyStore(), Log);
            Factory = new StubCustomStrategyFactory(problems, Log);
            Catalog = new FakeStrategyCatalog(Store, Factory);
        }

        public List<string> Log { get; } = [];

        public RecordingCustomStrategyStore Store { get; }

        public StubCustomStrategyFactory Factory { get; }

        public FakeStrategyCatalog Catalog { get; }

        public InMemoryTempDataProvider TempData { get; } = new();

        public StrategiesController Controller(string pathBase = "")
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.PathBase = pathBase;
            return new StrategiesController(Catalog, Store, Factory, NullLogger<StrategiesController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, TempData),
            };
        }

        public async Task<IActionResult> EditAsync(string id)
        {
            var controller = Controller();
            var result = await controller.Edit(id, CancellationToken.None);
            controller.TempData.Save();
            return result;
        }

        public async Task<IActionResult> SaveAsync(IDictionary<string, string> fields)
        {
            var controller = Controller();
            var form = new FormCollection(fields.ToDictionary(f => f.Key, f => new StringValues(f.Value)));
            var result = await controller.Save(form, CancellationToken.None);
            controller.TempData.Save();
            return result;
        }

        /// <summary>The GET of a redirect to the catalog, as the browser does.</summary>
        public async Task<StrategyCatalogViewModel> FollowAsync(IActionResult result)
        {
            var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
            redirect.ActionName.Should().Be(nameof(StrategiesController.Index));
            redirect.ControllerName.Should().BeNull();

            var controller = Controller();
            var view = (await controller.Index(CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
            controller.TempData.Save();
            return view.Model.Should().BeOfType<StrategyCatalogViewModel>().Subject;
        }
    }

    /// <summary>The fields of the blank form, as the browser sends them, with the given name.</summary>
    private static Dictionary<string, string> BlankFields(string name) => new()
    {
        ["Name"] = name,
        ["Description"] = "A test strategy.",
        ["Entry.Combinator"] = "All",
        ["Entry.Conditions[0].Left.Kind"] = "Close",
        ["Entry.Conditions[0].Operator"] = "CrossesAbove",
        ["Entry.Conditions[0].Right.Kind"] = "Sma",
        ["Entry.Conditions[0].Right.Period"] = "20",
        ["Exit.Combinator"] = "All",
        ["Exit.Conditions[0].Left.Kind"] = "Close",
        ["Exit.Conditions[0].Operator"] = "CrossesBelow",
        ["Exit.Conditions[0].Right.Kind"] = "Sma",
        ["Exit.Conditions[0].Right.Period"] = "20",
    };

    private static void AddEntryRow(Dictionary<string, string> fields, int index, string period)
    {
        fields[$"Entry.Conditions[{index}].Left.Kind"] = "Rsi";
        fields[$"Entry.Conditions[{index}].Left.Period"] = period;
        fields[$"Entry.Conditions[{index}].Operator"] = "LessThan";
        fields[$"Entry.Conditions[{index}].Right.Kind"] = "Constant";
        fields[$"Entry.Conditions[{index}].Right.Value"] = "30";
    }

    private static RuleBuilderViewModel BuilderOf(IActionResult result)
    {
        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.ViewName.Should().Be("Edit"); // one view for New, Edit and a refused Save
        return view.Model.Should().BeOfType<RuleBuilderViewModel>().Subject;
    }

    // ---- Routes (PART-4 section 5) ----

    [Fact] // PART-4 section 5, P4-U16: fixed routes; Save is the only POST and requires the antiforgery token
    public void Routes_AreTheFixedOnes_WithAntiforgeryOnSave()
    {
        var type = typeof(StrategiesController);
        var newAction = type.GetMethod(nameof(StrategiesController.New))!;
        var edit = type.GetMethod(nameof(StrategiesController.Edit))!;
        var save = type.GetMethod(nameof(StrategiesController.Save))!;

        newAction.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("/Strategies/Custom/New");
        newAction.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
        edit.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("/Strategies/Custom/{id}/Edit");
        edit.GetCustomAttribute<HttpPostAttribute>().Should().BeNull();
        save.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("/Strategies/Custom/Save");
        save.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
        save.GetCustomAttribute<HttpGetAttribute>().Should().BeNull();
    }

    // ---- GET /Strategies/Custom/New ----

    [Fact] // P4-U9: one condition per rule, editable name, no Delete
    public void New_ShowsTheBlankBuilder()
    {
        var model = BuilderOf(new Fixture().Controller().New());

        model.IsEdit.Should().BeFalse();
        model.Form.Id.Should().BeNull();
        model.Form.Name.Should().BeNull();
        model.Entry.Rows.Should().ContainSingle();
        model.Exit.Rows.Should().ContainSingle();
        model.Problems.Should().BeEmpty();
        model.DeleteUrl.Should().BeNull();
    }

    [Fact] // P4-U9, decision 4: the default rules of a new form are saved as they are, only the name is typed
    public async Task New_DefaultRules_AreSavedWithOnlyAName()
    {
        var fixture = new Fixture();
        var sma20 = new Operand(OperandKind.Sma, Period: 20);

        await fixture.SaveAsync(BlankFields("SMA cross"));

        fixture.Store.SavedDefinitions.Should().ContainSingle().Which.Should().BeEquivalentTo(new CustomStrategyDefinition(
            "custom-sma-cross",
            "SMA cross",
            "A test strategy.",
            new RuleSet(RuleCombinator.All, [new RuleCondition(new Operand(OperandKind.Close), ComparisonOperator.CrossesAbove, sma20)]),
            new RuleSet(RuleCombinator.All, [new RuleCondition(new Operand(OperandKind.Close), ComparisonOperator.CrossesBelow, sma20)])));
    }

    // ---- GET /Strategies/Custom/{id}/Edit ----

    [Fact] // P4-U9: the stored definition fills the form, name read-only, Delete offered
    public async Task Edit_Known_PrefillsFromTheStore()
    {
        var model = BuilderOf(await new Fixture().EditAsync(ExampleId));

        model.IsEdit.Should().BeTrue();
        model.Form.Id.Should().Be(ExampleId);
        model.Form.Name.Should().Be("Trend dip");
        model.Entry.Combinator.Should().Be("All");
        model.Entry.Rows.Select(r => r.Input).Should().Equal(
            new ConditionInput(new OperandInput("Close"), "GreaterThan", new OperandInput("Sma", Period: "200")),
            new ConditionInput(new OperandInput("Rsi", Period: "2"), "LessThan", new OperandInput("Constant", Value: "10")));
        model.Exit.Combinator.Should().Be("Any");
        model.Exit.Rows.Should().ContainSingle();
        model.Problems.Should().BeEmpty();
        model.DeleteUrl.Should().Be("/Strategies/Custom/custom-trend-dip/Delete");
    }

    [Theory] // P4-U9: unknown id → back to the catalog with a message, never an exception
    [InlineData("custom-does-not-exist")]
    [InlineData("does-not-exist")]
    public async Task Edit_Unknown_RedirectsWithAnError(string id)
    {
        var fixture = new Fixture();

        var catalog = await fixture.FollowAsync(await fixture.EditAsync(id));

        catalog.Notice.Should().Be(PageNotice.Error(StrategiesController.NotFoundMessage(id)));
    }

    [Fact] // P4-U9: a built-in strategy cannot be edited
    public async Task Edit_BuiltIn_RedirectsWithARefusal()
    {
        var fixture = new Fixture();

        var catalog = await fixture.FollowAsync(await fixture.EditAsync("fake-alternating"));

        catalog.Notice.Should().Be(PageNotice.Error(StrategiesController.BuiltInEditRefused));
    }

    [Fact] // P4-U9, P2-C1: a stored definition that fails Validate opens with its problems, so it can be repaired
    public async Task Edit_InvalidStoredDefinition_OpensWithItsProblems()
    {
        var fixture = new Fixture();
        var broken = FakeCustomStrategyStore.Example with
        {
            Id = "custom-broken",
            Name = "Broken",
            Entry = new RuleSet(RuleCombinator.All, []),
        };
        await fixture.Store.SaveAsync(broken); // ICustomStrategyStore does not validate
        fixture.Store.SavedDefinitions.Clear();

        var model = BuilderOf(await fixture.EditAsync("custom-broken"));

        model.ProblemsHeading.Should().Be(RuleBuilderViewModel.StoredInvalidHeading);
        model.Problems.Should().Equal(new FakeCustomStrategyFactory().Validate(broken));
        model.Entry.Rows.Should().BeEmpty();
    }

    // ---- POST /Strategies/Custom/Save: creation ----

    [Fact] // P4-U9: saved, then the card of the strategy (#strategy-{id}) with a success message, once
    public async Task Save_Create_SavesAndRedirectsToTheCard()
    {
        var fixture = new Fixture();

        var result = await fixture.SaveAsync(BlankFields("  RSI dip  "));

        result.Should().BeOfType<RedirectToActionResult>().Which.Fragment.Should().Be("strategy-custom-rsi-dip");
        fixture.Store.SavedDefinitions.Should().ContainSingle().Which.Name.Should().Be("RSI dip");
        var catalog = await fixture.FollowAsync(result);
        catalog.Notice.Should().Be(PageNotice.Success("The strategy \"RSI dip\" was created."));
        catalog.CustomStrategies.Select(c => c.Id).Should().Contain("custom-rsi-dip");
    }

    [Fact] // P4-U9, PART-4 section 3: Validate is called before SaveAsync, on the definition that is saved
    public async Task Save_ValidatesBeforeSaving()
    {
        var fixture = new Fixture();

        await fixture.SaveAsync(BlankFields("RSI dip"));

        fixture.Log.Should().Equal("Validate custom-rsi-dip", "SaveAsync custom-rsi-dip");
        fixture.Factory.Validated.Should().ContainSingle().Which.Should().BeSameAs(fixture.Store.SavedDefinitions.Single());
    }

    [Theory] // P4-U9: a new name whose id already exists is refused, the existing strategy is untouched
    [InlineData("Trend dip")]
    [InlineData("trend DIP!")]
    public async Task Save_Create_DuplicateName_IsRefused(string name)
    {
        var fixture = new Fixture();

        var model = BuilderOf(await fixture.SaveAsync(BlankFields(name)));

        model.Problems.Should().Equal(StrategiesController.DuplicateName);
        model.Form.Name.Should().Be(name);
        fixture.Store.SavedDefinitions.Should().BeEmpty();
        (await fixture.Store.GetAsync(ExampleId)).Should().Be(FakeCustomStrategyStore.Example);
    }

    [Fact] // P4-U9, decision 3: the duplicate name and the problems of Validate are shown together, duplicate first
    public async Task Save_Create_DuplicateAndValidateProblems_AreShownTogether()
    {
        var fixture = new Fixture(["Entry condition 1: the right operand needs a period between 1 and 500."]);

        var model = BuilderOf(await fixture.SaveAsync(BlankFields("Trend dip")));

        model.Problems.Should().Equal(
            StrategiesController.DuplicateName,
            "Entry condition 1: the right operand needs a period between 1 and 500.");
    }

    [Fact] // P4-U9: the messages of Validate are shown as they are, the input is kept, nothing is saved
    public async Task Save_ValidateProblems_AreShown_InputKept()
    {
        var fixture = new Fixture(["Entry condition 1: cannot compare two constants."]);
        var fields = BlankFields("My strategy");

        var model = BuilderOf(await fixture.SaveAsync(fields));

        model.ProblemsHeading.Should().Be(RuleBuilderViewModel.NotSavedHeading);
        model.Problems.Should().Equal("Entry condition 1: cannot compare two constants.");
        model.Form.Should().BeEquivalentTo(CustomStrategyForm.Read(new FormCollection(
            fields.ToDictionary(f => f.Key, f => new StringValues(f.Value)))));
        model.IsEdit.Should().BeFalse();
        fixture.Store.SavedDefinitions.Should().BeEmpty();
    }

    [Fact] // P4-U9: a format error shows the form again with every row, added ones included, and the raw text
    public async Task Save_FormatError_KeepsEveryRowAndTheRawText()
    {
        var fixture = new Fixture();
        var fields = BlankFields("My strategy");
        AddEntryRow(fields, 3, "abc"); // indexes 0, 3, 7: rule-builder.js leaves gaps after a removal
        AddEntryRow(fields, 7, "14");

        var model = BuilderOf(await fixture.SaveAsync(fields));

        model.Entry.Rows.Should().HaveCount(3);
        model.Entry.Rows[1].Input.Left.Period.Should().Be("abc");
        model.Entry.Rows[1].Error("Left.Period").Should().Be("Entry condition 2, left operand: the period must be a whole number.");
        model.Entry.Rows[2].Input.Left.Period.Should().Be("14");
        model.HasFieldErrors.Should().BeTrue();
        fixture.Factory.Validated.Should().BeEmpty();
        fixture.Store.SavedDefinitions.Should().BeEmpty();
    }

    [Fact] // P4-U9, R2: more rows than MaxConditionsPerRule are all sent to Validate, which reports them; never cut
    public async Task Save_TooManyRows_AreAllSentToValidate()
    {
        var fixture = new Fixture();
        var fields = BlankFields("My strategy");
        for (var i = 1; i <= 5; i++)
        {
            AddEntryRow(fields, i, "14");
        }

        var model = BuilderOf(await fixture.SaveAsync(fields));

        fixture.Factory.Validated.Single().Entry.Conditions.Should().HaveCount(6);
        model.Problems.Should().Equal(new FakeCustomStrategyFactory().Validate(fixture.Factory.Validated.Single()));
        model.Entry.Rows.Should().HaveCount(6);
    }

    [Theory] // P4-U9, R1: a name without letter or digit has no id: Validate's message, no exception
    [InlineData("!!!")]
    [InlineData("")]
    public async Task Save_NameWithoutLetterOrDigit_ShowsTheMessageOfValidate(string name)
    {
        var fixture = new Fixture();

        var model = BuilderOf(await fixture.SaveAsync(BlankFields(name)));

        fixture.Factory.Validated.Single().Id.Should().BeEmpty();
        model.Problems.Should().Equal(new FakeCustomStrategyFactory().Validate(fixture.Factory.Validated.Single()));
        model.Problems.Should().NotBeEmpty();
        fixture.Store.SavedDefinitions.Should().BeEmpty();
    }

    // ---- POST /Strategies/Custom/Save: modification ----

    [Fact] // P4-U9: the stored id and name are kept (the posted name is ignored), the definition is replaced
    public async Task Save_Edit_ReplacesTheDefinition_KeepsIdAndName()
    {
        var fixture = new Fixture();
        var fields = BlankFields("Renamed");
        fields["Id"] = ExampleId;

        var result = await fixture.SaveAsync(fields);

        result.Should().BeOfType<RedirectToActionResult>().Which.Fragment.Should().Be("strategy-custom-trend-dip");
        var saved = fixture.Store.SavedDefinitions.Should().ContainSingle().Subject;
        saved.Id.Should().Be(ExampleId);
        saved.Name.Should().Be("Trend dip");
        saved.Entry.Conditions.Single().Operator.Should().Be(ComparisonOperator.CrossesAbove);
        (await fixture.Store.GetAsync(ExampleId)).Should().BeSameAs(saved);
        (await fixture.FollowAsync(result)).Notice.Should().Be(PageNotice.Success("The strategy \"Trend dip\" was updated."));
    }

    [Fact] // P4-U9: a refused edit stays in edit mode with the stored name
    public async Task Save_Edit_Refused_StaysInEditMode()
    {
        var fixture = new Fixture(["Exit condition 1: the left operand cannot have a value."]);
        var fields = BlankFields("Renamed");
        fields["Id"] = ExampleId;

        var model = BuilderOf(await fixture.SaveAsync(fields));

        model.IsEdit.Should().BeTrue();
        model.Form.Name.Should().Be("Trend dip");
        model.DeleteUrl.Should().NotBeNull();
        fixture.Store.SavedDefinitions.Should().BeEmpty();
    }

    [Theory] // P4-U9: an edit of an unknown or built-in id goes back to the catalog; nothing validated nor saved
    [InlineData("custom-does-not-exist", false)]
    [InlineData("fake-threshold", true)]
    public async Task Save_Edit_UnknownOrBuiltIn_IsRefused(string id, bool builtIn)
    {
        var fixture = new Fixture();
        var fields = BlankFields("Whatever");
        fields["Id"] = id;

        var catalog = await fixture.FollowAsync(await fixture.SaveAsync(fields));

        catalog.Notice.Should().Be(PageNotice.Error(builtIn ? StrategiesController.BuiltInEditRefused : StrategiesController.NotFoundMessage(id)));
        fixture.Factory.Validated.Should().BeEmpty();
        fixture.Store.SavedDefinitions.Should().BeEmpty();
    }

    // ---- Catalog ----

    [Fact] // P4-U9, P4-U8: "New custom strategy" is offered even when there is no custom strategy yet
    public async Task Catalog_OffersNewCustomStrategy_EvenWhenEmpty()
    {
        var fixture = new Fixture();
        await fixture.Store.DeleteAsync(ExampleId);

        var controller = fixture.Controller("/app");
        var view = (await controller.Index(CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
        var model = view.Model.Should().BeOfType<StrategyCatalogViewModel>().Subject;

        model.CustomStrategies.Should().BeEmpty();
        model.NewCustomUrl.Should().Be("/app/Strategies/Custom/New");
    }
}
