using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using QuantBacktest.Web.Controllers;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Tests.Infrastructure;

public class NavigationTests
{
    [Fact] // P4-U13: the six links in order; Optimize is enabled by PR 8, the pages still to come stay disabled
    public void Navigation_OptimizeEnabled_OthersUnchanged()
    {
        Navigation.Items.Select(i => (i.Label, i.Enabled)).Should().Equal(
            ("Backtest", true),
            ("Compare", true),
            ("Optimize", true),
            ("Validate", false),
            ("Strategies", true),
            ("Data", false));
    }

    [Fact] // P4-U13: every enabled link points to a controller with a GET Index action
    public void Navigation_EnabledLinks_HaveAnIndexAction()
    {
        var controllers = typeof(CompareController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t))
            .ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (var item in Navigation.Items.Where(i => i.Enabled))
        {
            var controller = controllers[item.Controller + "Controller"];
            controller.GetMethod("Index")!.GetCustomAttribute<HttpGetAttribute>().Should().NotBeNull(item.Label);
        }
    }
}
