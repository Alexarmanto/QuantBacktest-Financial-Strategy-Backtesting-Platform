using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>One anomaly of the quality page: date (and its raw sort value), kind label and badge, description as is.</summary>
internal sealed record DataAnomalyRow(string Date, string SortValue, string Kind, string BadgeClass, string Description);

/// <summary>Every field of a DataQualityReport (P4-U10), formatted, and its anomalies in the order of the service (D7).</summary>
internal sealed record DataQualityReportView(
    string Symbol,
    string Timeframe,
    string Bars,
    string FirstDate,
    string LastDate,
    string Adjusted,
    string Origin,
    string LastUpdated,
    string MissingPeriods,
    string MissingPeriodsHelp,
    string LargestGap,
    IReadOnlyList<DataAnomalyRow> Anomalies)
{
    public static DataQualityReportView Create(DataQualityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new DataQualityReportView(
            report.Symbol,
            DataLabels.TimeframeLabel(report.Timeframe),
            DisplayFormat.Integer(report.BarCount),
            DisplayFormat.Date(report.FirstDate),
            DisplayFormat.Date(report.LastDate),
            DataLabels.Adjusted(report.IsAdjusted),
            DataLabels.Origin(report.Origin),
            DisplayFormat.UtcTimestamp(report.LastFetchedUtc),
            DisplayFormat.Integer(report.MissingPeriods),
            DataLabels.MissingPeriodsHelp(report.Timeframe),
            $"{DisplayFormat.Integer(report.LargestGapDays)} calendar {(report.LargestGapDays == 1 ? "day" : "days")}",
            report.Anomalies
                .Select(a => new DataAnomalyRow(
                    DisplayFormat.Date(a.Date),
                    a.Date.ToString(BacktestQuery.DateFormat, System.Globalization.CultureInfo.InvariantCulture),
                    DataLabels.AnomalyKind(a.Kind),
                    DataLabels.AnomalyBadgeClass(a.Kind),
                    a.Description))
                .ToList());
    }
}

/// <summary>
/// GET /Data/Quality (P4-U10). Three states: the link is invalid (InputErrors, nothing else: no service was called);
/// the report is unavailable (Alert, e.g. NotCached, with the refresh form); or the report (Report, with the form).
/// </summary>
internal sealed record DataQualityViewModel(
    string Heading,
    IReadOnlyList<string> InputErrors,
    PageNotice? Notice,
    FormAlert? Alert,
    DataQualityReportView? Report,
    DataRefreshFormViewModel? RefreshForm,
    string BackUrl)
{
    public const string InvalidHeading = "Data quality";
    public const string InvalidLinkMessage = "This quality report link is not valid:";

    public static DataQualityViewModel Invalid(IReadOnlyList<QueryFieldError> errors, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return new(InvalidHeading, errors.Select(e => e.Message).ToList(), null, null, null, null, IndexUrl(pathBase));
    }

    public static DataQualityViewModel Unavailable(
        DataSeriesQuery series, FormAlert alert, PageNotice? notice, DataRefreshFormViewModel refreshForm, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(refreshForm);
        return new(HeadingOf(series), [], notice, alert, null, refreshForm, IndexUrl(pathBase));
    }

    public static DataQualityViewModel ForReport(
        DataSeriesQuery series, DataQualityReport report, PageNotice? notice, DataRefreshFormViewModel refreshForm, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(refreshForm);
        return new(HeadingOf(series), [], notice, null, DataQualityReportView.Create(report), refreshForm, IndexUrl(pathBase));
    }

    /// <summary>"Data quality: JPM weekly".</summary>
    private static string HeadingOf(DataSeriesQuery series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return $"{InvalidHeading}: {series.Symbol} {BacktestQuery.FormatTimeframe(series.Timeframe)}";
    }

    private static string IndexUrl(string pathBase)
    {
        ArgumentNullException.ThrowIfNull(pathBase);
        return pathBase + DataRoutes.IndexPath;
    }
}
