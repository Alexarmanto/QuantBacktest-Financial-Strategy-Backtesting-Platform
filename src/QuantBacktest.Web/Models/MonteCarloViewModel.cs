using System.Globalization;
using Microsoft.AspNetCore.Http;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Infrastructure;

namespace QuantBacktest.Web.Models;

/// <summary>One line of the summary; the tone is only set on the actual final return (sign of a computed value).</summary>
internal sealed record MonteCarloSummaryRow(string Label, string Value, string Tone = ValueTone.Neutral);

/// <summary>One percentile of MC4 for both distributions.</summary>
internal sealed record PercentileRow(string Label, string FinalReturn, string FinalReturnTone, string MaxDrawdown);

/// <summary>One input of the GET form: the setting, the value as shown (typed or effective) and its server error.</summary>
internal sealed record MonteCarloField(NumberSetting Setting, string Value, string? Error);

/// <summary>
/// The sims / seed form. Hidden fields come from BacktestQuery.ToQueryPairs (canonical order) and come first, then
/// sims, then seed: the browser submits them in that order, which is exactly the canonical Monte Carlo URL.
/// </summary>
internal sealed record MonteCarloFormViewModel(
    string Action,
    IReadOnlyList<KeyValuePair<string, string>> Hidden,
    IReadOnlyList<MonteCarloField> Fields);

internal sealed record MonteCarloResultsViewModel(
    IReadOnlyList<MonteCarloSummaryRow> Summary,
    IReadOnlyList<PercentileRow> Percentiles,
    MonteCarloChartsViewModel Charts,
    string PdfUrl);

/// <summary>
/// Monte Carlo page (P4-X4, MC1 to MC7). Every number comes from IMonteCarloService: this model only formats
/// (DisplayFormat, P4-U14). Three states: results; a refused simulation (Alert, e.g. fewer than 5 trades, MC1);
/// invalid sims / seed (Header null: the backtest was not run, so there is no actual period).
/// </summary>
internal sealed record MonteCarloViewModel(
    string Symbol,
    string StrategyName,
    string Timeframe,
    ResultHeaderViewModel? Header,
    string ResultUrl,
    MonteCarloFormViewModel Form,
    FormAlert? Alert,
    MonteCarloResultsViewModel? Results)
{
    public const string Mc7Warning =
        "The Monte Carlo drawdown is measured trade by trade (MC7): it is not comparable with the bar-by-bar max drawdown "
        + "of the result page.";

    public const string MethodText =
        "Each simulation draws the returns of the trades of this backtest at random, with replacement, as many times as "
        + "there are trades, and compounds them. The spread of the results shows how much of the outcome may be luck.";

    public const string NotRunMessage = "The backtest was not run: correct the Monte Carlo settings below.";

    public const string ActualFinalReturnLabel = "Actual final return (trades compounded in their real order)";
    public const string ActualMaxDrawdownLabel = "Actual max drawdown (real trade order, trade by trade)";

    public static readonly NumberSetting SimulationsSetting = new(
        BacktestQuery.SimulationsKey,
        "Simulations",
        BacktestQuery.FormatInteger(BacktestQuery.MinMonteCarloSimulations),
        BacktestQuery.FormatInteger(BacktestQuery.MaxMonteCarloSimulations),
        null,
        false,
        $"Whole number, {BacktestQuery.FormatInteger(BacktestQuery.MinMonteCarloSimulations)} to "
            + $"{BacktestQuery.FormatInteger(BacktestQuery.MaxMonteCarloSimulations)}.",
        NumberSetting.WholeStep);

    public static readonly NumberSetting SeedSetting = new(
        BacktestQuery.SeedKey,
        "Seed",
        BacktestQuery.FormatInteger(int.MinValue),
        BacktestQuery.FormatInteger(int.MaxValue),
        null,
        false,
        "Whole number. The same seed always gives the same simulations.",
        NumberSetting.WholeStep);

    /// <summary>The simulation ran.</summary>
    public static MonteCarloViewModel Create(
        BacktestResult result,
        IStrategy strategy,
        BacktestQuery query,
        MonteCarloResult monteCarlo,
        string permalink,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(monteCarlo);
        var request = monteCarlo.Request;
        var summary = new List<MonteCarloSummaryRow>
        {
            new("Simulations", FormatCount(request.Simulations)),
            new("Seed", BacktestQuery.FormatInteger(request.Seed)),
            new("Trades resampled", FormatCount(monteCarlo.TradeCount)),
            new("Probability of loss", Percent(monteCarlo.ProbabilityOfLoss)),
            new(ActualFinalReturnLabel, Percent(monteCarlo.ActualFinalReturn), ValueTone.Of(Finite(monteCarlo.ActualFinalReturn))),
            new(ActualMaxDrawdownLabel, Percent(monteCarlo.ActualMaxDrawdown)),
        };

        var results = new MonteCarloResultsViewModel(
            summary,
            Percentiles(monteCarlo.FinalReturn, monteCarlo.MaxDrawdown),
            MonteCarloChartsViewModel.Create(monteCarlo),
            pathBase + ResultActionsViewModel.PdfPath + "?" + query.ToQueryString(request, forReport: true));

        return WithHeader(result, strategy, query, request, permalink, pathBase, alert: null, results);
    }

    /// <summary>The backtest ran but IMonteCarloService refused it (MC1: fewer than 5 trades): its message is shown.</summary>
    public static MonteCarloViewModel Refused(
        BacktestResult result,
        IStrategy strategy,
        BacktestQuery query,
        MonteCarloRequest request,
        BacktestValidationException exception,
        string permalink,
        string pathBase) =>
        WithHeader(result, strategy, query, request, permalink, pathBase, FormAlert.From(exception), results: null);

    /// <summary>
    /// sims or seed is invalid: nothing was run. The page keeps the input as typed with its error, the link back to
    /// the result, and no header data that would need the backtest (period, settings, Copy link).
    /// </summary>
    public static MonteCarloViewModel InvalidSettings(
        IStrategy strategy,
        BacktestQuery query,
        IQueryCollection received,
        IReadOnlyList<QueryFieldError> errors,
        string pathBase)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(errors);

        MonteCarloField Field(NumberSetting setting) => new(
            setting,
            received[setting.Key].ToString(),
            errors.FirstOrDefault(e => e.Field == setting.Key)?.Message);

        return new MonteCarloViewModel(
            query.Symbol,
            strategy.Name,
            query.Timeframe.ToString(),
            Header: null,
            BuildResultUrl(query, pathBase),
            BuildForm(query, pathBase, Field(SimulationsSetting), Field(SeedSetting)),
            Alert: null,
            Results: null);
    }

    /// <summary>Fraction as a percentage (DisplayFormat); a non-finite value is "n/a", never "NaN%".</summary>
    public static string Percent(double fraction) =>
        double.IsFinite(fraction) ? DisplayFormat.Percent(fraction) : DisplayFormat.NotAvailable;

    /// <summary>Whole count with thousands grouped: 10000 → "10,000".</summary>
    public static string FormatCount(int count) => count.ToString("#,##0", CultureInfo.InvariantCulture);

    private static MonteCarloViewModel WithHeader(
        BacktestResult result,
        IStrategy strategy,
        BacktestQuery query,
        MonteCarloRequest request,
        string permalink,
        string pathBase,
        FormAlert? alert,
        MonteCarloResultsViewModel? results)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pathBase);

        var header = ResultHeaderViewModel.Create(result, strategy, permalink);
        return new MonteCarloViewModel(
            header.Symbol,
            header.StrategyName,
            header.Timeframe,
            header,
            BuildResultUrl(query, pathBase),
            BuildForm(
                query,
                pathBase,
                new MonteCarloField(SimulationsSetting, BacktestQuery.FormatInteger(request.Simulations), null),
                new MonteCarloField(SeedSetting, BacktestQuery.FormatInteger(request.Seed), null)),
            alert,
            results);
    }

    private static string BuildResultUrl(BacktestQuery query, string pathBase) =>
        pathBase + ResultActionsViewModel.ResultPath + "?" + query.ToQueryString();

    private static MonteCarloFormViewModel BuildForm(BacktestQuery query, string pathBase, MonteCarloField simulations, MonteCarloField seed) =>
        new(pathBase + ResultActionsViewModel.MonteCarloPath, query.ToQueryPairs(), [simulations, seed]);

    /// <summary>MC4: p = 0.05, 0.25, 0.5, 0.75, 0.95, as returned by Core.</summary>
    private static List<PercentileRow> Percentiles(PercentileSummary finalReturn, PercentileSummary maxDrawdown) =>
    [
        Row("5th percentile", finalReturn.P5, maxDrawdown.P5),
        Row("25th percentile", finalReturn.P25, maxDrawdown.P25),
        Row("50th percentile (median)", finalReturn.P50, maxDrawdown.P50),
        Row("75th percentile", finalReturn.P75, maxDrawdown.P75),
        Row("95th percentile", finalReturn.P95, maxDrawdown.P95),
    ];

    private static PercentileRow Row(string label, double finalReturn, double maxDrawdown) =>
        new(label, Percent(finalReturn), ValueTone.Of(Finite(finalReturn)), Percent(maxDrawdown));

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
