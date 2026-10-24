using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.Logging.Abstractions;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.ViewComponents;

namespace QuantBacktest.Web.Tests.ViewComponents;

public class ApiQuotaViewComponentTests
{
    private static ApiQuotaViewComponent Create(IMarketDataService marketData) =>
        new(marketData, NullLogger<ApiQuotaViewComponent>.Instance)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext { HttpContext = new DefaultHttpContext() },
            },
        };

    [Fact]
    public async Task Quota_IsShownAsUsedOverLimit() // P4-U11
    {
        var marketData = new FakeMarketDataService();
        var quota = await marketData.GetApiQuotaAsync();

        var result = await Create(marketData).InvokeAsync();

        var model = result.Should().BeOfType<ViewViewComponentResult>()
            .Which.ViewData!.Model.Should().BeOfType<ApiQuotaBadgeViewModel>().Subject;
        model.Text.Should().Be($"API {quota.UsedToday}/{quota.DailyLimit} today");
    }

    [Fact]
    public void BadgeText_HasTheSpecifiedFormat() // P4-U11
    {
        new ApiQuotaBadgeViewModel(17, 25).Text.Should().Be("API 17/25 today");
    }

    [Fact]
    public async Task QuotaFailure_HidesTheBadge() // P4-U11
    {
        var result = await Create(new FailingQuotaService()).InvokeAsync();

        result.Should().BeOfType<ContentViewComponentResult>().Which.Content.Should().BeEmpty();
    }

    private sealed class FailingQuotaService : IMarketDataService
    {
        public Task<ApiQuotaStatus> GetApiQuotaAsync(CancellationToken cancellationToken = default) =>
            throw new MarketDataException(MarketDataErrorKind.NetworkError, "Quota store unavailable.");

        public Task<PriceSeries> GetSeriesAsync(string symbol, Timeframe timeframe, bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetCachedSymbolsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DataQualityReport> GetQualityReportAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
