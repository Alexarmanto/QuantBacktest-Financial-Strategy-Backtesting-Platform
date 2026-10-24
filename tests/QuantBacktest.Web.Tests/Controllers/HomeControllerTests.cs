using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Controllers;

public class HomeControllerTests
{
    [Fact]
    public void Index_RedirectsToBacktest() // PART-4 section 5, route GET /
    {
        var result = new HomeController().Index();

        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ControllerName.Should().Be("Backtest");
        redirect.ActionName.Should().Be("Index");
    }

    [Fact]
    public void Error_ShowsOnlyTheRequestId() // P4-U12
    {
        var controller = new HomeController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-42" },
            },
        };

        var result = controller.Error();

        result.Should().BeOfType<ViewResult>()
            .Which.Model.Should().Be(new ErrorViewModel("trace-42"));
    }
}
