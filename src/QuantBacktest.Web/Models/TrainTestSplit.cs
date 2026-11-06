using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The train/test split of T1 and T2 (docs/CONTRACT.md section 10, "Partie 4 orchestre"; PART-4 section 3 lists it
/// among the calculations the web may do). Counting and cutting bars only, nothing financial.
/// Every date is a bar date of the selected period, passed to the services as an inclusive bound (X1:
/// "barres avec From &lt;= date &lt;= To (bornes incluses …)"), so the engine selects exactly the same bars.
/// </summary>
internal sealed record TrainTestSplit(
    int BarCount,
    int TrainCount,
    DateOnly TrainFrom,
    DateOnly TrainTo,
    DateOnly TestFrom,
    DateOnly TestTo)
{
    /// <summary>T1: "il faut nTrain &gt;= 2 et N - nTrain &gt;= 2".</summary>
    public const int MinBarsPerPart = 2;

    public int TestCount => BarCount - TrainCount;

    /// <summary>
    /// T1: the N bars of the series with from &lt;= date &lt;= to (a null bound is open, as in X1), then
    /// nTrain = floor(N × trainPercent / 100), computed in decimal so 57% of 100 bars is 57, never 56.
    /// T2: train = bars 0..nTrain-1 of the period, test = bars nTrain..N-1.
    /// </summary>
    /// <param name="error">The English message when the period is empty or a part has fewer than 2 bars.</param>
    public static bool TryCreate(
        PriceSeries series,
        DateOnly? from,
        DateOnly? to,
        decimal trainPercent,
        [NotNullWhen(true)] out TrainTestSplit? split,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(series);
        split = null;

        var bars = series.Bars
            .Where(b => (from is not { } start || b.Date >= start) && (to is not { } end || b.Date <= end))
            .ToList();
        var count = bars.Count;
        if (count == 0)
        {
            error = NoBarsMessage(series);
            return false;
        }

        var trainCount = (int)decimal.Floor(count * trainPercent / 100m);
        if (trainCount < MinBarsPerPart || count - trainCount < MinBarsPerPart)
        {
            error = TooShortMessage(count, trainPercent, trainCount);
            return false;
        }

        split = new TrainTestSplit(
            count,
            trainCount,
            bars[0].Date,
            bars[trainCount - 1].Date,
            bars[trainCount].Date,
            bars[^1].Date);
        error = null;
        return true;
    }

    public static string NoBarsMessage(PriceSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        var name = $"{series.Symbol} ({BacktestQuery.FormatTimeframe(series.Timeframe)})";
        return series.Bars.Count == 0
            ? $"No bars are available for {name}."
            : $"The selected period contains no bars of {name}: its data runs from {Format(series.Bars[0].Date)} to {Format(series.Bars[^1].Date)}.";
    }

    public static string TooShortMessage(int count, decimal trainPercent, int trainCount) =>
        $"The selected period has {Bars(count)}: a training share of {BacktestQuery.FormatNumber(trainPercent)}% "
        + $"gives {Bars(trainCount, "training ")} and {Bars(count - trainCount, "test ")}, and each part needs at least "
        + $"{Bars(MinBarsPerPart)}. Choose a longer period or another training share.";

    private static string Bars(int count, string kind = "") => $"{Count(count)} {kind}{(count == 1 ? "bar" : "bars")}";

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static string Format(DateOnly date) => date.ToString(BacktestQuery.DateFormat, CultureInfo.InvariantCulture);
}
