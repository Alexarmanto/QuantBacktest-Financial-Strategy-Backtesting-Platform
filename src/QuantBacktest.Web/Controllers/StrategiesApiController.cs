using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

/// <summary>JSON of the strategy catalog for the dynamic backtest form (PART-4 section 5).</summary>
public sealed class StrategiesApiController(IStrategyCatalog catalog) : Controller
{
    /// <summary>GET /api/strategies: every strategy of the catalog, in catalog order.</summary>
    [HttpGet("/api/strategies")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var strategies = await catalog.GetAllAsync(cancellationToken);
        return Json(strategies.Select(StrategyApiModel.From).ToList());
    }
}
