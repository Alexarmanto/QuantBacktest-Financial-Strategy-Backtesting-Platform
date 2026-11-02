using Microsoft.AspNetCore.Mvc.ViewFeatures;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Web.Models;

/// <summary>Message shown once at the top of the catalog after a redirect (post/redirect/get), carried by TempData.</summary>
internal sealed record PageNotice(bool IsSuccess, string Text)
{
    private const string SuccessKey = "Notice.Success";
    private const string ErrorKey = "Notice.Error";

    public static PageNotice Success(string text) => new(true, text);

    public static PageNotice Error(string text) => new(false, text);

    public void WriteTo(ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[IsSuccess ? SuccessKey : ErrorKey] = Text;
    }

    /// <summary>Reads and consumes the notice; null when there is none.</summary>
    public static PageNotice? ReadFrom(ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        if (tempData[ErrorKey] is string error)
        {
            return Error(error);
        }

        return tempData[SuccessKey] is string success ? Success(success) : null;
    }
}

/// <summary>The strategy catalog page (P4-U8): built-in cards, then custom cards, in catalog order.</summary>
internal sealed record StrategyCatalogViewModel(
    IReadOnlyList<StrategyCardViewModel> BuiltInStrategies,
    IReadOnlyList<StrategyCardViewModel> CustomStrategies,
    PageNotice? Notice)
{
    public const string NoBuiltInStrategies = "No built-in strategy is available.";

    public const string NoCustomStrategies = "No custom strategy yet.";

    /// <summary>Ids of the two section titles, targets of the in-page navigation (card anchors start with "strategy-").</summary>
    public const string BuiltInAnchor = "built-in";

    public const string CustomAnchor = "custom";

    public int BuiltInCount => BuiltInStrategies.Count;

    public int CustomCount => CustomStrategies.Count;

    /// <summary>In-page navigation links, always both, even at zero: "Built-in (11)", "Custom (0)".</summary>
    public string BuiltInLinkText => $"Built-in ({BuiltInCount})";

    public string CustomLinkText => $"Custom ({CustomCount})";

    public static StrategyCatalogViewModel Create(IReadOnlyList<IStrategy> strategies, PageNotice? notice, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(pathBase);

        var (builtIn, custom) = StrategyGroups.Split(strategies);
        return new StrategyCatalogViewModel(
            builtIn.Select(s => StrategyCardViewModel.Create(s, pathBase)).ToList(),
            custom.Select(s => StrategyCardViewModel.Create(s, pathBase)).ToList(),
            notice);
    }
}
