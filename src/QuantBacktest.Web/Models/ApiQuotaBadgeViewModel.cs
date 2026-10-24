namespace QuantBacktest.Web.Models;

/// <summary>Navigation badge of P4-U11: "API 17/25 today".</summary>
internal sealed record ApiQuotaBadgeViewModel(int UsedToday, int DailyLimit)
{
    public string Text => FormattableString.Invariant($"API {UsedToday}/{DailyLimit} today");
}
