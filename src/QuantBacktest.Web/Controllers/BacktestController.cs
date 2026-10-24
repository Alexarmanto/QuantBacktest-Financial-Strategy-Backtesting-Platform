using Microsoft.AspNetCore.Mvc;

namespace QuantBacktest.Web.Controllers;

public sealed class BacktestController : Controller
{
    /// <summary>GET /Backtest. Placeholder until the backtest form (PR 2, P4-U1).</summary>
    [HttpGet]
    public IActionResult Index() => View();
}
