using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// One cached series as it travels in the URLs of the data pages (P4-U10): the symbol, normalized in upper case, and
/// the timeframe. BacktestQuery reads and writes it (P4-U16); this record only builds the links of the pages.
/// </summary>
internal sealed record DataSeriesQuery(string Symbol, Timeframe Timeframe)
{
    /// <summary>Canonical query string, without the leading "?": symbol, then tf.</summary>
    public string ToQueryString() => BacktestQuery.ToDataQueryString(this);

    /// <summary>The quality page of this series: /Data/Quality?symbol=AAPL&amp;tf=daily.</summary>
    public string QualityUrl(string pathBase) => $"{pathBase}{DataRoutes.QualityPath}?{ToQueryString()}";

    /// <summary>The data page with the refresh form prefilled for this series: /Data?symbol=AAPL&amp;tf=daily#refresh.</summary>
    public string RefreshFormUrl(string pathBase) => $"{pathBase}{DataRoutes.IndexPath}?{ToQueryString()}{DataRoutes.RefreshAnchor}";
}

/// <summary>The frozen routes of the data pages (PART-4 section 5).</summary>
internal static class DataRoutes
{
    public const string IndexPath = "/Data";
    public const string QualityPath = "/Data/Quality";

    /// <summary>Id of the refresh section of /Data: a redirect to the form scrolls to it (its alerts are at its top).</summary>
    public const string RefreshAnchorId = "refresh";
    public const string RefreshAnchor = "#" + RefreshAnchorId;
}
