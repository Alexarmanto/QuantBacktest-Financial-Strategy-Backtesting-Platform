using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>
/// Strategy catalog (P4-U8) and deletion of custom strategies (PART-4 section 5). Thin: IStrategyCatalog lists,
/// ICustomStrategyStore deletes, the view models format. A refused deletion is a message, never an exception.
/// </summary>
public sealed class StrategiesController(
    IStrategyCatalog catalog,
    ICustomStrategyStore store,
    ILogger<StrategiesController> logger) : Controller
{
    public const string DeletePath = "/Strategies/Custom/{id}/Delete";

    public const string BuiltInRefused = "Built-in strategies cannot be deleted.";

    /// <summary>GET /Strategies: one card per strategy of the catalog, built-in first, then custom.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var strategies = await catalog.GetAllAsync(cancellationToken);
        return View(StrategyCatalogViewModel.Create(strategies, PageNotice.ReadFrom(TempData), Request.PathBase.Value ?? string.Empty));
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
        if (!Regex.IsMatch(id, Conventions.CustomStrategyIdPattern, RegexOptions.CultureInvariant))
        {
            var strategy = await catalog.FindAsync(id, cancellationToken);
            notice = PageNotice.Error(strategy is null ? NotFoundMessage(id) : BuiltInRefused);
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

        notice.WriteTo(TempData);
        return RedirectToAction(nameof(Index));
    }

    public static string NotFoundMessage(string id) => $"The strategy '{id}' was not found. It may have been deleted already.";
}
