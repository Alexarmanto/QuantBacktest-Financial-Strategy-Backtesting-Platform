namespace QuantBacktest.Web.Infrastructure;

/// <summary>One link of the main navigation; a page that does not exist yet stays visible but disabled.</summary>
internal sealed record NavigationItem(string Label, string Controller, bool Enabled);

/// <summary>Navigation of P4-U13, in display order, printed by _Layout. Each page's PR enables its link.</summary>
internal static class Navigation
{
    public static IReadOnlyList<NavigationItem> Items { get; } =
    [
        new("Backtest", "Backtest", true),
        new("Compare", "Compare", true),
        new("Optimize", "Optimize", true),
        new("Validate", "Validate", false),
        new("Strategies", "Strategies", true),
        new("Data", "Data", false),
    ];
}
