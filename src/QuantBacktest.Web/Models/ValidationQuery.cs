using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Web.Models;

/// <summary>
/// The settings of a train/test validation (P4-X3, docs/CONTRACT.md T1 to T3) as they travel in its permalink: the
/// heatmap settings and the training share in percent. Built only by <see cref="BacktestQuery.TryParseValidation"/>.
/// It builds the requests of the three runs from a <see cref="TrainTestSplit"/>: same grid, same execution, same
/// other parameters; only the period and the two best values change. No financial logic.
/// </summary>
internal sealed class ValidationQuery
{
    internal ValidationQuery(OptimizationQuery optimization, decimal trainPercent)
    {
        Optimization = optimization;
        TrainPercent = trainPercent;
    }

    /// <summary>The heatmap settings, on the requested period (from, to of the URL).</summary>
    public OptimizationQuery Optimization { get; }

    /// <summary>Training share in percent (URL unit), in [50, 90].</summary>
    public decimal TrainPercent { get; }

    public BacktestQuery Settings => Optimization.Settings;

    public string StrategyId => Optimization.StrategyId;

    public string Symbol => Optimization.Symbol;

    public Timeframe Timeframe => Optimization.Timeframe;

    public SweepMetric Metric => Optimization.Metric;

    /// <summary>The checks of the heatmap (<see cref="OptimizationQuery.Resolve"/>); the training share is already checked.</summary>
    public IReadOnlyList<QueryFieldError> Resolve(IStrategy strategy, out ValidationQuery? effective)
    {
        var errors = Optimization.Resolve(strategy, out var optimization);
        effective = optimization is null ? null : new ValidationQuery(optimization, TrainPercent);
        return errors;
    }

    /// <summary>Canonical query string of /Validate/Result (and of the prefilled form), without the leading "?".</summary>
    public string ToQueryString() =>
        Settings.ToValidationQueryString(Optimization.X, Optimization.Y, Optimization.Metric, TrainPercent);

    public bool IsCanonical(QueryString received) =>
        Settings.IsCanonicalValidation(received, Optimization.X, Optimization.Y, Optimization.Metric, TrainPercent);

    /// <summary>
    /// The heatmap of the training period (T3, "heatmap sur le train"): its ToSweepRequest() is the sweep, and its
    /// ToQueryString() the canonical /Optimize/Result of the full, clickable heatmap.
    /// </summary>
    public OptimizationQuery TrainGrid(TrainTestSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        return Optimization.WithPeriod(split.TrainFrom, split.TrainTo);
    }

    /// <summary>
    /// The backtest of the best cell on the training period: the BaseRequest of the sweep with the two best values.
    /// Not in the "Calcul" of P4-X3 nor in T3 ("un seul backtest sur le test"), which name only the test backtest:
    /// it is the only way, without a contract change, to show the full train metrics and the train equity curve that
    /// the "Affichage" of P4-X3 requires (SweepResult carries one metric per cell and no curve). Decision of PR 9.
    /// </summary>
    public BacktestQuery TrainBacktest(TrainTestSplit split, SweepCell best)
    {
        ArgumentNullException.ThrowIfNull(best);
        return TrainGrid(split).ForCell(best.X, best.Y);
    }

    /// <summary>T3: "un seul backtest sur le test avec ces paramètres et les mêmes réglages d'exécution".</summary>
    public BacktestQuery TestBacktest(TrainTestSplit split, SweepCell best)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(best);
        return Optimization.WithPeriod(split.TestFrom, split.TestTo).ForCell(best.X, best.Y);
    }
}
