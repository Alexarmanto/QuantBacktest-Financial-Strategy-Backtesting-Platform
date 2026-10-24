using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Web.Controllers;

namespace QuantBacktest.Web.Tests.Controllers;

public class BacktestControllerTests
{
    [Fact]
    public void Index_ReturnsTheBacktestView() // PART-4 section 5, route GET /Backtest
    {
        new BacktestController().Index().Should().BeOfType<ViewResult>();
    }
}
