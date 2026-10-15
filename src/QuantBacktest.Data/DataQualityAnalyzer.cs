using System.Globalization;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.Storage;

namespace QuantBacktest.Data;

/// <summary>Quality diagnostics of a cached series, exactly as D7 defines them (P1-D15).</summary>
internal static class DataQualityAnalyzer
{
    private const decimal PriceJumpThreshold = 0.25m;
    private const int StaleCloseCount = 5;
    private const int DailyGapDays = 5;
    private const int WeeklyGapDays = 10;

    public static DataQualityReport Analyze(CachedSeries cached)
    {
        ArgumentNullException.ThrowIfNull(cached);

        var series = cached.Series;
        var bars = series.Bars;
        var gapThreshold = series.Timeframe == Timeframe.Daily ? DailyGapDays : WeeklyGapDays;
        var anomalies = new List<DataAnomaly>();
        var largestGap = 0;
        var weeklyMissing = 0;
        var identicalCloses = 1;

        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (bar.Volume == 0)
            {
                anomalies.Add(new DataAnomaly(bar.Date, DataAnomalyKind.ZeroVolume, "Volume is zero."));
            }

            if (i == 0)
            {
                continue;
            }

            var previous = bars[i - 1];
            var change = bar.Close / previous.Close - 1m;
            if (Math.Abs(change) > PriceJumpThreshold)
            {
                anomalies.Add(new DataAnomaly(bar.Date, DataAnomalyKind.PriceJump, string.Create(
                    CultureInfo.InvariantCulture,
                    $"Close changed by {(double)change * 100:+0.0;-0.0} % in one bar (possible split or bad tick).")));
            }

            // One anomaly per sequence, dated the 5th identical close.
            identicalCloses = bar.Close == previous.Close ? identicalCloses + 1 : 1;
            if (identicalCloses == StaleCloseCount)
            {
                anomalies.Add(new DataAnomaly(bar.Date, DataAnomalyKind.StalePrice, $"Close unchanged for {StaleCloseCount} consecutive bars."));
            }

            var gap = bar.Date.DayNumber - previous.Date.DayNumber;
            largestGap = Math.Max(largestGap, gap);
            weeklyMissing += Math.Max(0, (int)Math.Round(gap / 7.0) - 1);
            if (gap > gapThreshold)
            {
                anomalies.Add(new DataAnomaly(bar.Date, DataAnomalyKind.Gap, $"{gap} calendar days without data."));
            }
        }

        var missingPeriods = series.Timeframe == Timeframe.Daily ? MissingWeekdays(bars) : weeklyMissing;

        return new DataQualityReport(
            series.Symbol,
            series.Timeframe,
            bars.Count,
            bars[0].Date,
            bars[^1].Date,
            series.IsAdjusted,
            cached.Origin,
            cached.LastFetchedUtc,
            missingPeriods,
            largestGap,
            [.. anomalies.OrderBy(anomaly => anomaly.Date).ThenBy(anomaly => anomaly.Kind)]);
    }

    /// <summary>D7 daily: weekdays (Monday to Friday) between the first and the last date without a bar.</summary>
    private static int MissingWeekdays(IReadOnlyList<PriceBar> bars)
    {
        var weekdays = 0;
        for (var date = bars[0].Date; date <= bars[^1].Date; date = date.AddDays(1))
        {
            if (IsWeekday(date))
            {
                weekdays++;
            }
        }

        return weekdays - bars.Count(bar => IsWeekday(bar.Date));
    }

    private static bool IsWeekday(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
