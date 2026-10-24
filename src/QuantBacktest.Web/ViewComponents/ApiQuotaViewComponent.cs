using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.ViewComponents;

/// <summary>Navigation badge "API 17/25 today" (P4-U11), hidden when the quota cannot be read.</summary>
public sealed class ApiQuotaViewComponent(
    IMarketDataService marketData,
    ILogger<ApiQuotaViewComponent> logger) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        try
        {
            var quota = await marketData.GetApiQuotaAsync(HttpContext.RequestAborted);
            return View(new ApiQuotaBadgeViewModel(quota.UsedToday, quota.DailyLimit));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The badge is cosmetic: a failure must not break every page, but it is logged.
            logger.LogWarning(exception, "API quota unavailable; the navigation badge is hidden.");
            return Content(string.Empty);
        }
    }
}
