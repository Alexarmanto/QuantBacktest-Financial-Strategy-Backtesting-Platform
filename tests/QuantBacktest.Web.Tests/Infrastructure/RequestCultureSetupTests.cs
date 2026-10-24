using Microsoft.AspNetCore.Builder;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Tests.Infrastructure;

public class RequestCultureSetupTests
{
    [Fact]
    public void Configure_UsesEnUsOnly() // P4-U14
    {
        var options = new RequestLocalizationOptions();

        RequestCultureSetup.Configure(options);

        options.DefaultRequestCulture.Culture.Name.Should().Be("en-US");
        options.DefaultRequestCulture.UICulture.Name.Should().Be("en-US");
        options.SupportedCultures!.Select(c => c.Name).Should().Equal("en-US");
        options.SupportedUICultures!.Select(c => c.Name).Should().Equal("en-US");
        options.RequestCultureProviders.Should().BeEmpty();
    }
}
