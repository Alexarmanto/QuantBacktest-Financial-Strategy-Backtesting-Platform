using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// Built-in strategies first, then custom ones (P4-U1, P4-U8), each group in catalog order. A strategy is custom when
/// its category is Custom (R8): the backtest form list and the catalog page group them the same way.
/// </summary>
internal static class StrategyGroups
{
    public static bool IsCustom(IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        return strategy.Category == StrategyCategory.Custom;
    }

    public static (IReadOnlyList<IStrategy> BuiltIn, IReadOnlyList<IStrategy> Custom) Split(IEnumerable<IStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var all = strategies.ToList();
        return (all.Where(s => !IsCustom(s)).ToList(), all.Where(IsCustom).ToList());
    }
}
