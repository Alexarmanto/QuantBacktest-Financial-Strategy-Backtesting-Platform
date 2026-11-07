using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The words of the data pages (P4-U10): labels of the contract enums and fixed texts. Badge colours come from the
/// palette of the exit reasons (ResultTradesViewModel): never green or red, which mean gain and loss (site.css).
/// </summary>
internal static class DataLabels
{
    /// <summary>P4-U10: the cells of a series without cached data.</summary>
    public const string NotCached = "not cached";

    public const string QualityLink = "Quality report";
    public const string FillRefreshFormLink = "Fill refresh form";
    public const string NoAnomaly = "No anomaly found.";

    public const string EmptyCache =
        "No series is cached yet. Run a backtest to import the seed files, or refresh a series below.";

    public const string LastUpdatedHelp = "When the series was last written to the cache (seed import or API refresh).";

    /// <summary>Rows of GET /Data: daily, then weekly, for each symbol.</summary>
    public static IReadOnlyList<Timeframe> Timeframes { get; } = [Timeframe.Daily, Timeframe.Weekly];

    public static string TimeframeLabel(Timeframe timeframe) => timeframe switch
    {
        Core.Models.Timeframe.Daily => "Daily",
        Core.Models.Timeframe.Weekly => "Weekly",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };

    public static string Origin(DataOrigin origin) => origin switch
    {
        DataOrigin.Api => "API",
        DataOrigin.Seed => "Seed file",
        DataOrigin.Mixed => "Mixed (seed + API)",
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown data origin."),
    };

    public static string Adjusted(bool isAdjusted) => isAdjusted ? "Yes" : "No";

    public static string AnomalyKind(DataAnomalyKind kind) => kind switch
    {
        DataAnomalyKind.PriceJump => "Price jump",
        DataAnomalyKind.ZeroVolume => "Zero volume",
        DataAnomalyKind.StalePrice => "Stale price",
        DataAnomalyKind.Gap => "Gap",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown anomaly kind."),
    };

    /// <summary>One distinct Bootstrap badge per kind, in the palette of the exit reasons.</summary>
    public static string AnomalyBadgeClass(DataAnomalyKind kind) => kind switch
    {
        DataAnomalyKind.PriceJump => "text-bg-warning",
        DataAnomalyKind.ZeroVolume => "text-bg-primary",
        DataAnomalyKind.StalePrice => "text-bg-secondary",
        DataAnomalyKind.Gap => "text-bg-info",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown anomaly kind."),
    };

    /// <summary>What MissingPeriods counts (DataQualityReport comment, docs/CONTRACT.md D7).</summary>
    public static string MissingPeriodsHelp(Timeframe timeframe) => timeframe switch
    {
        Core.Models.Timeframe.Daily =>
            "Weekdays (Monday to Friday) without a bar between the first and last dates; market holidays are included, so a few are normal.",
        Core.Models.Timeframe.Weekly => "Expected weekly bars that are missing between the first and last dates.",
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };
}
