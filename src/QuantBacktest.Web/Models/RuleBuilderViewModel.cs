using QuantBacktest.Core;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One condition row of the rule builder. Prefix is the start of its field names ("Entry.Conditions[0]") and IdPrefix
/// of its element ids ("entry-0"); both hold "__index__" in the row template that rule-builder.js clones.
/// </summary>
internal sealed record ConditionRowViewModel(
    string Prefix,
    string IdPrefix,
    int Number,
    ConditionInput Input,
    IReadOnlyDictionary<string, string> Errors)
{
    public const string IndexPlaceholder = "__index__";

    public string Name(string part) => Prefix + "." + part;

    public string ElementId(string part) => IdPrefix + "-" + part.Replace('.', '-').ToLowerInvariant();

    public string? Error(string part) => Errors.GetValueOrDefault(Name(part));
}

/// <summary>
/// One rule (entry or exit) of the builder: its combinator, its rows, the template of a new row and the bounds of
/// its number of rows (data-min and data-max of rule-builder.js, which greys Remove and Add with them).
/// </summary>
internal sealed record RuleSectionViewModel(
    string Key,
    string Title,
    string Verb,
    string? Combinator,
    string? CombinatorError,
    IReadOnlyList<ConditionRowViewModel> Rows,
    ConditionRowViewModel Template,
    int MinConditions,
    int MaxConditions)
{
    public string CombinatorName => CustomStrategyForm.CombinatorField(Key);

    public string IdPrefix => Key.ToLowerInvariant();

    /// <summary>The number of the counter; the only part rule-builder.js rewrites.</summary>
    public int ConditionCount => Rows.Count;

    public string CountSuffix => FormattableString.Invariant($" of {MaxConditions} conditions (minimum {MinConditions})");

    /// <summary>"3 of 5 conditions (minimum 1)", as rendered before any script runs.</summary>
    public string CountText => FormattableString.Invariant($"{ConditionCount}") + CountSuffix;
}

/// <summary>
/// The rule builder page (P4-U9), shared by New and Edit. In edit mode the name is read-only (the id never changes)
/// and the page offers Delete. Problems are the messages of the server and of ICustomStrategyFactory.Validate, shown
/// as they are; FieldErrors are the format errors of CustomStrategyForm, shown under their field.
/// </summary>
internal sealed record RuleBuilderViewModel(
    bool IsEdit,
    CustomStrategyForm Form,
    string? ProblemsHeading,
    IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, string> FieldErrors,
    string PathBase)
{
    public const string NewTitle = "New custom strategy";
    public const string EditTitle = "Edit custom strategy";
    public const string NotSavedHeading = "The strategy was not saved:";
    public const string StoredInvalidHeading = "This saved strategy is invalid, so the catalog hides it. Fix these problems, then save:";
    public const string FormatHeading = "Please correct the highlighted fields.";
    public const string PermalinkNote = "Saving replaces the rules used by every existing link to this strategy.";
    public const string NameReadOnlyNote = "The name cannot change: it defines the id of the strategy.";
    public const string NoScriptNote = "Adding and removing conditions needs JavaScript. The other fields work without it.";

    public string Title => IsEdit ? EditTitle : NewTitle;

    /// <summary>R2: a rule has at least one condition (the contract has no constant for it).</summary>
    public const int MinConditions = 1;

    public int MaxConditions => Conventions.MaxConditionsPerRule;

    public string SaveUrl => PathBase + StrategyCardViewModel.CustomPathPrefix + "Save";

    /// <summary>Back to the card of the strategy in edit mode, to the custom section otherwise.</summary>
    public string CancelUrl => PathBase + "/Strategies#" + (IsEdit && Form.Id is { } id
        ? StrategyCardViewModel.AnchorOf(id)
        : StrategyCatalogViewModel.CustomAnchor);

    /// <summary>The POST of the catalog (decision 5), edit mode only.</summary>
    public string? DeleteUrl => IsEdit && Form.Id is { } id ? StrategyCardViewModel.CustomUrl(PathBase, id, "Delete") : null;

    public string? DeleteConfirmation => IsEdit ? StrategyCardViewModel.ConfirmDeletion(Form.Name ?? Form.Id ?? string.Empty) : null;

    public bool HasFieldErrors => FieldErrors.Count > 0;

    public string? Error(string fieldName) => FieldErrors.GetValueOrDefault(fieldName);

    public RuleSectionViewModel Entry => Section(CustomStrategyForm.EntryKey, "Entry rule", "Buy when", Form.Entry);

    public RuleSectionViewModel Exit => Section(CustomStrategyForm.ExitKey, "Exit rule", "Sell when", Form.Exit);

    public static RuleBuilderViewModel ForNew(string pathBase) =>
        new(false, CustomStrategyForm.Blank(), null, [], new Dictionary<string, string>(), pathBase);

    /// <summary>A stored definition; problems not empty when it no longer passes Validate.</summary>
    public static RuleBuilderViewModel ForEdit(CustomStrategyDefinition definition, IReadOnlyList<string> problems, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(problems);
        return new(
            true,
            CustomStrategyForm.FromDefinition(definition),
            problems.Count > 0 ? StoredInvalidHeading : null,
            problems,
            new Dictionary<string, string>(),
            pathBase);
    }

    /// <summary>A refused POST: the form exactly as it was sent, with the problems or the format errors.</summary>
    public static RuleBuilderViewModel Refused(
        bool isEdit,
        CustomStrategyForm form,
        IReadOnlyList<string> problems,
        IReadOnlyDictionary<string, string> fieldErrors,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(fieldErrors);
        return new(isEdit, form, problems.Count > 0 ? NotSavedHeading : null, problems, fieldErrors, pathBase);
    }

    private RuleSectionViewModel Section(string key, string title, string verb, RuleInput rule)
    {
        var idPrefix = key.ToLowerInvariant();
        var rows = rule.Conditions
            .Select((condition, i) => new ConditionRowViewModel(
                $"{key}.Conditions[{i}]", $"{idPrefix}-{i}", i + 1, condition, FieldErrors))
            .ToList();
        var template = new ConditionRowViewModel(
            $"{key}.Conditions[{ConditionRowViewModel.IndexPlaceholder}]",
            $"{idPrefix}-{ConditionRowViewModel.IndexPlaceholder}",
            0,
            CustomStrategyForm.DefaultCondition,
            new Dictionary<string, string>());
        return new RuleSectionViewModel(
            key, title, verb, rule.Combinator, Error(CustomStrategyForm.CombinatorField(key)), rows, template,
            MinConditions, MaxConditions);
    }
}
