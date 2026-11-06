using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Abstractions;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The train/test form (P4-X3): the heatmap form (same model, same partials, same checks) plus the training share.
/// The share is a field of the heatmap model (extraFieldNames), so its error is shown under its input and counts in
/// HasErrors; its value is kept as typed (P4-U12).
/// </summary>
internal sealed record ValidateFormViewModel(OptimizeFormViewModel Sweep, string TrainValue)
{
    public static int MaxCells => OptimizeFormViewModel.MaxCells;

    /// <summary>The input of train: [50, 90] in percent, any decimal, required.</summary>
    public static NumberSetting Train { get; } = new(
        BacktestQuery.TrainKey,
        "Training share (%)",
        BacktestQuery.FormatNumber(BacktestQuery.MinTrainPercent),
        BacktestQuery.FormatNumber(BacktestQuery.MaxTrainPercent),
        null,
        true,
        $"{BacktestQuery.FormatNumber(BacktestQuery.MinTrainPercent)} to {BacktestQuery.FormatNumber(BacktestQuery.MaxTrainPercent)}% "
            + "of the bars of the period train the grid; the rest tests the best cell.");

    public string? TrainError => Sweep.Error(BacktestQuery.TrainKey);

    public static ValidateFormViewModel Create(
        IQueryCollection query,
        IReadOnlyList<IStrategy> strategies,
        IReadOnlyList<string> cachedSymbols,
        IReadOnlyList<QueryFieldError> errors,
        FormAlert? alert)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sweep = OptimizeFormViewModel.Create(query, strategies, cachedSymbols, errors, alert, [BacktestQuery.TrainKey]);
        var train = query.ContainsKey(BacktestQuery.TrainKey)
            ? query[BacktestQuery.TrainKey].ToString()
            : BacktestQuery.FormatNumber(BacktestQuery.DefaultTrainPercent)!;
        return new ValidateFormViewModel(sweep, train);
    }
}
