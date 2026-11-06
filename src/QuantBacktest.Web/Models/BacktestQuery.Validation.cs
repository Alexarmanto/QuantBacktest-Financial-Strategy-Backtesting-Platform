using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using QuantBacktest.Core;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The train/test part of the permalink (P4-X3, docs/CONTRACT.md T1 to T3): the URL of the heatmap plus train, the
/// training share in percent. Read by the same Reader as every other setting, written after metric.
/// </summary>
internal sealed partial record BacktestQuery
{
    // P4-X3: "part d'entraînement (train, en pour cent, 50 à 90, défaut 70)".
    public const string TrainKey = "train";

    // T1: "trainFraction dans [0.5, 0.9]". The bounds are only written in docs/CONTRACT.md (T1), not in Conventions:
    // they are spelled once here, in URL units (percent).
    public const decimal MinTrainPercent = 50m;
    public const decimal MaxTrainPercent = 90m;

    /// <summary>T1: "défaut Conventions.DefaultTrainFraction" (0.7), in percent: 70.</summary>
    public static readonly decimal DefaultTrainPercent = (decimal)Conventions.DefaultTrainFraction * 100m;

    public static readonly string TrainRangeMessage =
        $"Training share must be between {FormatNumber(MinTrainPercent)}% and {FormatNumber(MaxTrainPercent)}%.";

    /// <summary>
    /// Reads the URL of the train/test page (P4-X3): exactly what <see cref="TryParseOptimization"/> reads, by the same
    /// code, then train (absent: the default of Conventions; any decimal in [50, 90]). Never throws on user input.
    /// </summary>
    public static bool TryParseValidation(
        IQueryCollection query,
        [NotNullWhen(true)] out ValidationQuery? result,
        out IReadOnlyList<QueryFieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reader = new Reader(query);
        var optimization = ReadOptimization(reader);

        var train = reader.Number(TrainKey, "Training share");
        if (train is < MinTrainPercent or > MaxTrainPercent)
        {
            reader.Error(TrainKey, TrainRangeMessage);
        }

        errors = reader.Errors;
        if (errors.Count > 0)
        {
            result = null;
            return false;
        }

        result = new ValidationQuery(optimization, train ?? DefaultTrainPercent);
        return true;
    }

    /// <summary>
    /// Canonical query string of the train/test page, without the leading "?": the one of
    /// <see cref="ToOptimizationQueryString"/>, then train in its shortest form (70.0 → 70).
    /// </summary>
    public string ToValidationQueryString(SweepAxisInput x, SweepAxisInput y, SweepMetric metric, decimal trainPercent)
    {
        var pairs = OptimizationPairs(x, y, metric);
        pairs.Add(new(TrainKey, FormatNumber(trainPercent)!));
        return Encode(pairs);
    }

    /// <summary>True when the received query string is exactly <see cref="ToValidationQueryString"/> once both are decoded.</summary>
    public bool IsCanonicalValidation(QueryString received, SweepAxisInput x, SweepAxisInput y, SweepMetric metric, decimal trainPercent) =>
        DecodePairs(received.Value).SequenceEqual(DecodePairs(ToValidationQueryString(x, y, metric, trainPercent)));
}
