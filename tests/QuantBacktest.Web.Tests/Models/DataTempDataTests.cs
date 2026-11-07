using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Net.Http.Headers;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

/// <summary>
/// What POST /Data/Refresh hands to GET /Data (P4-U10, P4-U12) through the real cookie TempData of the app: its
/// serializer only takes simple types, so these tests write the cookie and read it back, in memory.
/// </summary>
public class DataTempDataTests
{
    /// <summary>Saves the values with the app's provider, then loads them from the cookie into a new request.</summary>
    private static ITempDataDictionary RoundTrip(Action<ITempDataDictionary> write)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { ApplicationName = "QuantBacktest.Web" });
        services.AddLogging();
        services.AddControllersWithViews();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        using var provider = services.BuildServiceProvider();
        var cookies = provider.GetRequiredService<ITempDataProvider>();
        cookies.Should().BeOfType<CookieTempDataProvider>();

        var post = new DefaultHttpContext { RequestServices = provider };
        var saved = new TempDataDictionary(post, cookies);
        write(saved);
        saved.Save();

        var setCookie = SetCookieHeaderValue.ParseList(post.Response.Headers.SetCookie.Select(v => v!).ToList()).Single();
        var get = new DefaultHttpContext { RequestServices = provider };
        get.Request.Headers.Cookie = $"{setCookie.Name}={setCookie.Value}";
        var loaded = new TempDataDictionary(get, cookies);
        loaded.Load();
        return loaded;
    }

    [Theory] // P4-U12: message, hint and command survive the cookie; a missing part stays missing
    [InlineData("The daily limit is reached.", FormAlert.RateLimitHint, null)]
    [InlineData("No key.", FormAlert.MissingApiKeyRetryHint, FormAlert.MissingApiKeyCommand)]
    public void Alert_RoundTripThroughTheCookieTempDataProvider(string message, string? hint, string? command)
    {
        var alert = new FormAlert(message, hint, command);

        var loaded = RoundTrip(tempData => DataTempData.WriteAlert(tempData, alert));

        DataTempData.ReadAlert(loaded).Should().Be(alert);
    }

    [Fact] // P4-U10: the field errors of the refresh form survive the cookie, the first one per field
    public void FieldErrors_RoundTripThroughTheCookieTempDataProvider()
    {
        var loaded = RoundTrip(tempData => DataTempData.WriteFieldErrors(tempData,
        [
            new QueryFieldError("symbol", "Symbol is required."),
            new QueryFieldError("symbol", "Second error of the same field."),
            new QueryFieldError("tf", "Timeframe must be 'daily' or 'weekly'."),
        ]));

        DataTempData.ReadFieldErrors(loaded).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["symbol"] = "Symbol is required.",
            ["tf"] = "Timeframe must be 'daily' or 'weekly'.",
        });
    }

    [Fact] // post/redirect/get: the alert and the errors are shown once, then gone
    public void Read_ConsumesTheValues()
    {
        var provider = new InMemoryTempDataProvider();
        var post = new TempDataDictionary(new DefaultHttpContext(), provider);
        DataTempData.WriteAlert(post, new FormAlert("Refused."));
        DataTempData.WriteFieldErrors(post, [new QueryFieldError("symbol", "Symbol is required.")]);
        post.Save();

        var first = new TempDataDictionary(new DefaultHttpContext(), provider);
        DataTempData.ReadAlert(first).Should().Be(new FormAlert("Refused."));
        DataTempData.ReadFieldErrors(first).Should().ContainKey("symbol");
        first.Save();

        var second = new TempDataDictionary(new DefaultHttpContext(), provider);
        DataTempData.ReadAlert(second).Should().BeNull();
        DataTempData.ReadFieldErrors(second).Should().BeEmpty();
    }
}
