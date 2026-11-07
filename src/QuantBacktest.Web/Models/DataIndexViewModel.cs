using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>One symbol × timeframe of GET /Data, with its quality report, or null when the series is not cached.</summary>
internal sealed record DataSeriesEntry(string Symbol, Timeframe Timeframe, DataQualityReport? Report);

/// <summary>
/// One row of the table of GET /Data (P4-U10), formatted. A series without cached data shows "not cached" and the
/// link that prefills the refresh form instead of the quality link.
/// </summary>
internal sealed record DataSeriesRow(
    string Symbol,
    string Timeframe,
    bool IsCached,
    string Bars,
    string FirstDate,
    string LastDate,
    string Origin,
    string Adjusted,
    string Anomalies,
    string? QualityUrl,
    string? RefreshFormUrl);

/// <summary>GET /Data (P4-U10, P4-U11): the cached series, the API quota and the refresh form.</summary>
internal sealed record DataIndexViewModel(
    IReadOnlyList<DataSeriesRow> Rows,
    string QuotaUsage,
    string QuotaRemaining,
    string QuotaResetsAt,
    DataRefreshFormViewModel RefreshForm)
{
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Rows in the order of the entries (the service's symbol order, daily then weekly). Only formatting.</summary>
    public static DataIndexViewModel Create(
        IReadOnlyList<DataSeriesEntry> entries,
        ApiQuotaStatus quota,
        DataRefreshFormViewModel refreshForm,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(refreshForm);
        ArgumentNullException.ThrowIfNull(pathBase);

        var rows = entries.Select(entry => Row(entry, pathBase)).ToList();
        return new DataIndexViewModel(
            rows,
            $"{DisplayFormat.Integer(quota.UsedToday)} / {DisplayFormat.Integer(quota.DailyLimit)}",
            $"{DisplayFormat.Integer(quota.Remaining)} left today",
            DisplayFormat.UtcTimestamp(quota.ResetsAtUtc),
            refreshForm);
    }

    private static DataSeriesRow Row(DataSeriesEntry entry, string pathBase)
    {
        var series = new DataSeriesQuery(entry.Symbol, entry.Timeframe);
        var timeframe = DataLabels.TimeframeLabel(entry.Timeframe);
        if (entry.Report is not { } report)
        {
            return new DataSeriesRow(
                entry.Symbol, timeframe, false,
                DataLabels.NotCached, DataLabels.NotCached, DataLabels.NotCached,
                DataLabels.NotCached, DataLabels.NotCached, DataLabels.NotCached,
                null, series.RefreshFormUrl(pathBase));
        }

        return new DataSeriesRow(
            entry.Symbol,
            timeframe,
            true,
            DisplayFormat.Integer(report.BarCount),
            DisplayFormat.Date(report.FirstDate),
            DisplayFormat.Date(report.LastDate),
            DataLabels.Origin(report.Origin),
            DataLabels.Adjusted(report.IsAdjusted),
            DisplayFormat.Integer(report.Anomalies.Count),
            series.QualityUrl(pathBase),
            null);
    }
}
