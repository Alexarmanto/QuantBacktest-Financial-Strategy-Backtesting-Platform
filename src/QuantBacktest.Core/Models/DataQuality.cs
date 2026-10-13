namespace QuantBacktest.Core.Models;

/// <summary>Where the cached bars of a series come from.</summary>
public enum DataOrigin
{
    Api = 0,
    Seed = 1,
    Mixed = 2,
}

public enum DataAnomalyKind
{
    /// <summary>|Close / previous Close - 1| above 25 %: possible split or bad tick.</summary>
    PriceJump = 0,

    /// <summary>Volume equal to zero.</summary>
    ZeroVolume = 1,

    /// <summary>At least 5 consecutive identical closes.</summary>
    StalePrice = 2,

    /// <summary>Calendar gap between two bars larger than expected (daily: more than 5 days, weekly: more than 10 days).</summary>
    Gap = 3,
}

/// <summary>One suspicious point found in a cached series. Description is English and safe to display.</summary>
public sealed record DataAnomaly(DateOnly Date, DataAnomalyKind Kind, string Description);

/// <summary>
/// Quality diagnostics of a cached series (exact rules: docs/CONTRACT.md section 7).
/// MissingPeriods: daily = weekdays without a bar between FirstDate and LastDate (market holidays included);
/// weekly = expected weekly bars that are missing.
/// </summary>
public sealed record DataQualityReport(
    string Symbol,
    Timeframe Timeframe,
    int BarCount,
    DateOnly FirstDate,
    DateOnly LastDate,
    bool IsAdjusted,
    DataOrigin Origin,
    DateTimeOffset LastFetchedUtc,
    int MissingPeriods,
    int LargestGapDays,
    IReadOnlyList<DataAnomaly> Anomalies);

/// <summary>Alpha Vantage calls made today (UTC day) by this application.</summary>
public sealed record ApiQuotaStatus(int UsedToday, int DailyLimit, DateTimeOffset ResetsAtUtc)
{
    public int Remaining => Math.Max(0, DailyLimit - UsedToday);
}
