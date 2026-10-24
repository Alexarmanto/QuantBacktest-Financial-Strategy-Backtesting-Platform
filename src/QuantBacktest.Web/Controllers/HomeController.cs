using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Controllers;

// Controllers are public: MVC only discovers public classes.
public sealed class HomeController : Controller
{
    /// <summary>GET / → /Backtest.</summary>
    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(BacktestController.Index), "Backtest");

    /// <summary>GET /Home/Error: target of UseExceptionHandler outside Development (P4-U12).</summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel(HttpContext.TraceIdentifier));
}
