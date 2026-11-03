using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Strategy catalog (P4-U8), rule builder (P4-U9) and deletion of custom strategies (PART-4 section 5). Thin:
/// IStrategyCatalog lists, ICustomStrategyFactory validates, ICustomStrategyStore reads, saves and deletes, the view
/// models format. A refused request is a message, never an exception.
/// </summary>
public sealed class StrategiesController(
    IStrategyCatalog catalog,
    ICustomStrategyStore store,
    ICustomStrategyFactory factory,
    ILogger<StrategiesController> logger) : Controller
{
    public const string NewPath = "/Strategies/Custom/New";

    public const string EditPath = "/Strategies/Custom/{id}/Edit";

    public const string SavePath = "/Strategies/Custom/Save";

    public const string DeletePath = "/Strategies/Custom/{id}/Delete";

    public const string BuiltInRefused = "Built-in strategies cannot be deleted.";

    public const string BuiltInEditRefused = "Built-in strategies cannot be edited.";

    public const string DuplicateName = "A strategy with this name already exists.";

    private string PathBase => Request.PathBase.Value ?? string.Empty;

    /// <summary>GET /Strategies: one card per strategy of the catalog, built-in first, then custom.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var strategies = await catalog.GetAllAsync(cancellationToken);
        return View(StrategyCatalogViewModel.Create(strategies, PageNotice.ReadFrom(TempData), PathBase));
    }

    /// <summary>GET /Strategies/Custom/New: an empty rule builder whose default rules can be saved as they are.</summary>
    [HttpGet(NewPath)]
    public IActionResult New() => View(nameof(Edit), RuleBuilderViewModel.ForNew(PathBase));

    /// <summary>
    /// GET /Strategies/Custom/{id}/Edit. The definition is read from the store, not from the catalog, so a stored
    /// definition that no longer passes Validate (hidden by the catalog, P2-C1) can still be opened and repaired.
    /// Unknown or built-in id: back to the catalog with a message.
    /// </summary>
    [HttpGet(EditPath)]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var (definition, refusal) = await FindCustomAsync(id, cancellationToken);
        if (definition is null)
        {
            return BackToCatalog(PageNotice.Error(refusal!));
        }

        return View(nameof(Edit), RuleBuilderViewModel.ForEdit(definition, factory.Validate(definition), PathBase));
    }

    /// <summary>
    /// POST /Strategies/Custom/Save (P4-U9). Format errors first (shown under their field). Then the id comes from
    /// Conventions.CreateCustomStrategyId (creation) or from the store (edit: the stored name and id are kept, the
    /// posted name is ignored). A new name whose id exists and the problems of Validate are shown together; the form
    /// is shown again as it was sent. Otherwise SaveAsync, then the card of the strategy with a message (PRG).
    /// </summary>
    [HttpPost(SavePath)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IFormCollection form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var input = CustomStrategyForm.Read(form);
        var isEdit = !string.IsNullOrEmpty(input.Id);
        string id;
        string? idProblem = null;
        if (isEdit)
        {
            var (stored, refusal) = await FindCustomAsync(input.Id!, cancellationToken);
            if (stored is null)
            {
                return BackToCatalog(PageNotice.Error(refusal!));
            }

            input = input with { Id = stored.Id, Name = stored.Name };
            id = stored.Id;
        }
        else
        {
            try
            {
                id = Conventions.CreateCustomStrategyId(input.Name ?? string.Empty);
            }
            catch (ArgumentException exception)
            {
                // A name without letter or digit has no id: Validate reports it in its own words (R1).
                id = string.Empty;
                idProblem = exception.Message;
            }
        }

        var formatErrors = input.TryBuild(id, out var definition);
        if (definition is null)
        {
            return Refused(isEdit, input, [], formatErrors);
        }

        var problems = new List<string>();
        if (!isEdit && id.Length > 0 && await store.GetAsync(id, cancellationToken) is not null)
        {
            problems.Add(DuplicateName);
        }

        problems.AddRange(factory.Validate(definition));
        if (idProblem is not null && problems.Count == 0)
        {
            problems.Add(idProblem);
        }

        if (problems.Count > 0)
        {
            return Refused(isEdit, input, problems, formatErrors);
        }

        await store.SaveAsync(definition, cancellationToken);
        logger.LogInformation("Custom strategy {StrategyId} saved.", id);
        PageNotice.Success($"The strategy \"{definition.Name}\" was {(isEdit ? "updated" : "created")}.").WriteTo(TempData);
        return RedirectToAction(nameof(Index), null, null, StrategyCardViewModel.AnchorOf(id));
    }

    /// <summary>
    /// POST /Strategies/Custom/{id}/Delete, then back to the catalog with a message. An id outside the custom pattern
    /// is never sent to the store: the catalog tells a built-in strategy (refused) from an unknown id. A custom id
    /// goes straight to the store, so a stored definition that the catalog cannot build can still be deleted.
    /// </summary>
    [HttpPost(DeletePath)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        PageNotice notice;
        if (!IsCustomId(id))
        {
            notice = PageNotice.Error(await RefuseNonCustomAsync(id, BuiltInRefused, cancellationToken));
        }
        else
        {
            var definition = await store.GetAsync(id, cancellationToken);
            if (await store.DeleteAsync(id, cancellationToken))
            {
                logger.LogInformation("Custom strategy {StrategyId} deleted.", id);
                notice = PageNotice.Success($"The strategy \"{definition?.Name ?? id}\" was deleted.");
            }
            else
            {
                notice = PageNotice.Error(NotFoundMessage(id));
            }
        }

        return BackToCatalog(notice);
    }

    public static string NotFoundMessage(string id) => $"The strategy '{id}' was not found. It may have been deleted already.";

    private static bool IsCustomId(string id) => Regex.IsMatch(id, Conventions.CustomStrategyIdPattern, RegexOptions.CultureInvariant);

    /// <summary>The stored definition of a custom id, or the message that refuses it (built-in or unknown).</summary>
    private async Task<(CustomStrategyDefinition? Definition, string? Refusal)> FindCustomAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsCustomId(id))
        {
            return (null, await RefuseNonCustomAsync(id, BuiltInEditRefused, cancellationToken));
        }

        var definition = await store.GetAsync(id, cancellationToken);
        return definition is null ? (null, NotFoundMessage(id)) : (definition, null);
    }

    private async Task<string> RefuseNonCustomAsync(string id, string builtInMessage, CancellationToken cancellationToken) =>
        await catalog.FindAsync(id, cancellationToken) is null ? NotFoundMessage(id) : builtInMessage;

    private RedirectToActionResult BackToCatalog(PageNotice notice)
    {
        notice.WriteTo(TempData);
        return RedirectToAction(nameof(Index));
    }

    private ViewResult Refused(
        bool isEdit,
        CustomStrategyForm input,
        IReadOnlyList<string> problems,
        IReadOnlyDictionary<string, string> formatErrors) =>
        View(nameof(Edit), RuleBuilderViewModel.Refused(isEdit, input, problems, formatErrors, PathBase));
}
