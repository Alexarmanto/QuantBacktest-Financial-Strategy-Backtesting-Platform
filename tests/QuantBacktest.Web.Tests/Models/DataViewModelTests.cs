using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;

namespace QuantBacktest.Web.Tests.Models;

/// <summary>The view models of the data pages (P4-U10): only formatting of what the service returned.</summary>
public class DataViewModelTests
{
    private static readonly DateTimeOffset Fetched = new(2026, 11, 6, 23, 3, 53, TimeSpan.Zero);

    internal static DataQualityReport Report(string symbol, Timeframe timeframe, params DataAnomaly[] anomalies) => new(
        symbol, timeframe, 1406, new DateOnly(1999, 11, 12), new DateOnly(2026, 10, 14), true, DataOrigin.Seed, Fetched,
        MissingPeriods: 1, LargestGapDays: 11, anomalies);

    private static ApiQuotaStatus Quota(int used) => new(used, 25, new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));

    private static DataRefreshFormViewModel EmptyForm(int remaining = 22) =>
        DataRefreshFormViewModel.ForInput(string.Empty, Timeframe.Daily, [], new Dictionary<string, string>(), null, remaining);

    private static DataIndexViewModel Index(params DataSeriesEntry[] entries) =>
        DataIndexViewModel.Create(entries, Quota(3), EmptyForm(), pathBase: string.Empty);

    [Fact] // P4-U10: one row per entry, in the order given (service symbol order, daily then weekly)
    public void Index_OneRowPerEntry_InTheGivenOrder()
    {
        var model = Index(
            new DataSeriesEntry("AAPL", Timeframe.Daily, Report("AAPL", Timeframe.Daily)),
            new DataSeriesEntry("AAPL", Timeframe.Weekly, Report("AAPL", Timeframe.Weekly)),
            new DataSeriesEntry("JPM", Timeframe.Daily, null));

        model.Rows.Select(r => (r.Symbol, r.Timeframe)).Should().Equal(("AAPL", "Daily"), ("AAPL", "Weekly"), ("JPM", "Daily"));
        model.IsEmpty.Should().BeFalse();
    }

    [Fact] // P4-U10, P4-U14: bars, dates, origin, adjusted, anomaly count, quality link
    public void Index_CachedRow_FormatsEveryCell()
    {
        var anomalies = new[] { new DataAnomaly(new DateOnly(2001, 9, 21), DataAnomalyKind.Gap, "11 calendar days without data.") };

        var row = Index(new DataSeriesEntry("JPM", Timeframe.Weekly, Report("JPM", Timeframe.Weekly, anomalies))).Rows.Single();

        row.Should().Be(new DataSeriesRow(
            "JPM", "Weekly", true, "1,406", "1999-11-12", "2026-10-14", "Seed file", "Yes", "1",
            "/Data/Quality?symbol=JPM&tf=weekly", null));
    }

    [Fact] // P4-U10: NotCached → "not cached", no quality link, a link that prefills the refresh form
    public void Index_NotCachedRow_TextAndPrefillLink_NoQualityLink()
    {
        var row = DataIndexViewModel.Create([new DataSeriesEntry("IBM", Timeframe.Weekly, null)], Quota(3), EmptyForm(), "/app").Rows.Single();

        row.IsCached.Should().BeFalse();
        new[] { row.Bars, row.FirstDate, row.LastDate, row.Origin, row.Adjusted, row.Anomalies }.Should().AllBe("not cached");
        row.QualityUrl.Should().BeNull();
        row.RefreshFormUrl.Should().Be("/app/Data?symbol=IBM&tf=weekly#refresh");
    }

    [Fact] // P4-U10, D8: used / limit, remaining, next UTC midnight
    public void Index_Quota_UsedLimitRemainingAndUtcReset()
    {
        var model = Index();

        model.QuotaUsage.Should().Be("3 / 25");
        model.QuotaRemaining.Should().Be("22 left today");
        model.QuotaResetsAt.Should().Be("2026-11-08 00:00 UTC");
        model.IsEmpty.Should().BeTrue();
    }

    [Theory] // P4-U10: exact text of the confirmation, N = ApiQuotaStatus.Remaining (0 included: the form stays active)
    [InlineData(22, "This uses one of your daily API calls (22 left today). Continue?")]
    [InlineData(1, "This uses one of your daily API calls (1 left today). Continue?")]
    [InlineData(0, "This uses one of your daily API calls (0 left today). Continue?")]
    public void RefreshConfirmation_ExactText_WithRemaining(int remaining, string expected)
    {
        DataRefreshFormViewModel.ConfirmationText(remaining).Should().Be(expected);
        EmptyForm(remaining).Confirmation.Should().Be(expected);
    }

    [Fact] // P4-U10: every field of DataQualityReport, formatted
    public void Quality_EveryReportField_Formatted()
    {
        var view = DataQualityReportView.Create(Report("JPM", Timeframe.Weekly));

        view.Should().BeEquivalentTo(new
        {
            Symbol = "JPM",
            Timeframe = "Weekly",
            Bars = "1,406",
            FirstDate = "1999-11-12",
            LastDate = "2026-10-14",
            Adjusted = "Yes",
            Origin = "Seed file",
            LastUpdated = "2026-11-06 23:03 UTC",
            MissingPeriods = "1",
            LargestGap = "11 calendar days",
        });
    }

    [Fact] // D7, P4-U10: anomalies in the order of the service, label, badge, raw sort value, description as is
    public void Quality_Anomalies_ServiceOrder_DescriptionAsIs()
    {
        var view = DataQualityReportView.Create(Report(
            "JPM",
            Timeframe.Weekly,
            new DataAnomaly(new DateOnly(2001, 9, 21), DataAnomalyKind.Gap, "11 calendar days without data."),
            new DataAnomaly(new DateOnly(2008, 3, 20), DataAnomalyKind.PriceJump, "Close changed by +25.8 % in one bar (possible split or bad tick).")));

        view.Anomalies.Should().Equal(
            new DataAnomalyRow("2001-09-21", "2001-09-21", "Gap", "text-bg-info", "11 calendar days without data."),
            new DataAnomalyRow("2008-03-20", "2008-03-20", "Price jump", "text-bg-warning", "Close changed by +25.8 % in one bar (possible split or bad tick)."));
    }

    [Fact] // P4-U10: a report without anomaly; the view prints the message
    public void Quality_NoAnomaly_EmptyListAndMessage()
    {
        DataQualityReportView.Create(Report("AAPL", Timeframe.Daily)).Anomalies.Should().BeEmpty();
        DataLabels.NoAnomaly.Should().Be("No anomaly found.");
    }

    [Theory] // D7 and the DataQualityReport comment: what MissingPeriods counts
    [InlineData(Timeframe.Daily, "Weekdays (Monday to Friday) without a bar between the first and last dates; market holidays are included, so a few are normal.")]
    [InlineData(Timeframe.Weekly, "Expected weekly bars that are missing between the first and last dates.")]
    public void Quality_MissingPeriodsHelp_PerTimeframe(Timeframe timeframe, string expected)
    {
        DataQualityReportView.Create(Report("AAPL", timeframe)).MissingPeriodsHelp.Should().Be(expected);
    }

    [Theory] // P4-U10: labels of DataOrigin
    [InlineData(DataOrigin.Api, "API")]
    [InlineData(DataOrigin.Seed, "Seed file")]
    [InlineData(DataOrigin.Mixed, "Mixed (seed + API)")]
    public void OriginLabel_EachValue(DataOrigin origin, string expected)
    {
        DataLabels.Origin(origin).Should().Be(expected);
    }

    [Theory] // P4-U10: labels of DataAnomalyKind; badges never green or red (gain and loss)
    [InlineData(DataAnomalyKind.PriceJump, "Price jump", "text-bg-warning")]
    [InlineData(DataAnomalyKind.ZeroVolume, "Zero volume", "text-bg-primary")]
    [InlineData(DataAnomalyKind.StalePrice, "Stale price", "text-bg-secondary")]
    [InlineData(DataAnomalyKind.Gap, "Gap", "text-bg-info")]
    public void AnomalyKind_LabelAndBadge_NeverGreenOrRed(DataAnomalyKind kind, string label, string badge)
    {
        DataLabels.AnomalyKind(kind).Should().Be(label);
        DataLabels.AnomalyBadgeClass(kind).Should().Be(badge).And.NotContain("success").And.NotContain("danger");
    }

    [Theory] // P4-U10: adjusted or not
    [InlineData(true, "Yes")]
    [InlineData(false, "No")]
    public void AdjustedLabel_YesNo(bool isAdjusted, string expected)
    {
        DataLabels.Adjusted(isAdjusted).Should().Be(expected);
    }
}
