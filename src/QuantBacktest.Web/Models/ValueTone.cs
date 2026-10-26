namespace QuantBacktest.Web.Models;

/// <summary>
/// CSS class of a value that is already computed, chosen from its sign only (P4-U4, P4-U6). Green and red are
/// reserved for gains and losses (site.css); zero is neutral.
/// </summary>
internal static class ValueTone
{
    public const string Gain = "text-gain";
    public const string Loss = "text-loss";
    public const string Neutral = "";

    public static string Of(double value) => value > 0 ? Gain : value < 0 ? Loss : Neutral;
}
