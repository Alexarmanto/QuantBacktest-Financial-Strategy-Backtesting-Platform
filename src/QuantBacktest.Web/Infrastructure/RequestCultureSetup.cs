using Microsoft.AspNetCore.Localization;

namespace QuantBacktest.Web.Infrastructure;

/// <summary>Request localization with en-US only (P4-U14): the browser language never changes the formats.</summary>
internal static class RequestCultureSetup
{
    public const string CultureName = "en-US";

    public static void Configure(RequestLocalizationOptions options)
    {
        options.SetDefaultCulture(CultureName);
        options.SupportedCultures = [new(CultureName)];
        options.SupportedUICultures = [new(CultureName)];
        options.RequestCultureProviders.Clear();
    }
}
