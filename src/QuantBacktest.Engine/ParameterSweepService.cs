using System.Globalization;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>
/// The optimisation heatmap (docs/CONTRACT.md section 6): one backtest per pair of parameter values.
/// The prices are loaded once and the cells are computed one after the other, in row-major order.
/// </summary>
internal sealed class ParameterSweepService : IParameterSweepService
{
    private readonly IMarketDataService _marketData;
    private readonly IStrategyCatalog _catalog;
    private readonly IBacktestEngine _engine;

    public ParameterSweepService(IMarketDataService marketData, IStrategyCatalog catalog, IBacktestEngine engine)
    {
        _marketData = marketData;
        _catalog = catalog;
        _engine = engine;
    }

    public async Task<SweepResult> RunAsync(SweepRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BaseRequest);
        ArgumentNullException.ThrowIfNull(request.X);
        ArgumentNullException.ThrowIfNull(request.Y);

        BacktestRequest baseRequest = request.BaseRequest;

        // the strategy is resolved once
        IStrategy strategy = await _catalog.FindAsync(baseRequest.StrategyId, cancellationToken)
            ?? throw new BacktestValidationException($"Unknown strategy '{baseRequest.StrategyId}'.");

        // O1: the axes
        List<double> xValues = BuildAxis(request.X, strategy, "X");
        List<double> yValues = BuildAxis(request.Y, strategy, "Y");
        if (request.X.ParameterKey == request.Y.ParameterKey)
        {
            throw new BacktestValidationException("The two axes must use two different parameters.");
        }

        if (xValues.Count * yValues.Count > Conventions.MaxSweepCells)
        {
            throw new BacktestValidationException(
                $"The heatmap would have {xValues.Count * yValues.Count} cells; the maximum is {Conventions.MaxSweepCells}.");
        }

        // the two swept parameters are replaced in every cell, so their value in the base request does not matter
        Dictionary<string, double> otherParameters = new(baseRequest.Parameters);
        otherParameters.Remove(request.X.ParameterKey);
        otherParameters.Remove(request.Y.ParameterKey);
        BacktestRequest completedBase = RequestValidator.ValidateAndComplete(
            baseRequest with { Parameters = otherParameters }, strategy);

        // the data is loaded once
        string symbol = RequestValidator.NormalizeSymbol(baseRequest.Symbol);
        PriceSeries series = await _marketData.GetSeriesAsync(symbol, baseRequest.Timeframe, false, cancellationToken);

        // O2 and O5: row by row, for each Y every X
        List<SweepCell> cells = new(xValues.Count * yValues.Count);
        foreach (double y in yValues)
        {
            foreach (double x in xValues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                cells.Add(RunCell(completedBase, request, strategy, series, x, y));
            }
        }

        // O3 and O4
        SweepSummary summary = SweepAnalysis.Summarize(xValues.Count, yValues.Count, cells);

        return new SweepResult(
            Request: request,
            XValues: xValues,
            YValues: yValues,
            Cells: cells,
            Best: summary.Best,
            BestNeighborhoodAverage: summary.BestNeighborhoodAverage,
            IsIsolatedPeak: summary.IsIsolatedPeak);
    }

    private SweepCell RunCell(
        BacktestRequest completedBase, SweepRequest sweep, IStrategy strategy, PriceSeries series, double x, double y)
    {
        Dictionary<string, double> parameters = new(completedBase.Parameters)
        {
            [sweep.X.ParameterKey] = x,
            [sweep.Y.ParameterKey] = y,
        };

        try
        {
            // the cell is validated and simulated exactly like a normal backtest
            BacktestRequest cellRequest = RequestValidator.ValidateAndComplete(
                completedBase with { Parameters = parameters }, strategy);
            BacktestResult result = _engine.Run(cellRequest, series, strategy);

            return new SweepCell(x, y, ReadMetric(result.Metrics, sweep.Metric), result.Metrics.TradeCount, null);
        }
        catch (BacktestValidationException error)
        {
            // an invalid combination (for example fast >= slow) is shown as an empty cell, it does not stop the grid
            return new SweepCell(x, y, null, 0, error.Message);
        }
    }

    private static double? ReadMetric(PerformanceMetrics metrics, SweepMetric metric)
    {
        return metric switch
        {
            SweepMetric.SharpeRatio => metrics.SharpeRatio,
            SweepMetric.SortinoRatio => metrics.SortinoRatio,
            SweepMetric.CalmarRatio => metrics.CalmarRatio,
            SweepMetric.CumulativeReturn => metrics.CumulativeReturn,
            SweepMetric.AnnualizedReturn => metrics.AnnualizedReturn,
            SweepMetric.ProfitFactor => metrics.ProfitFactor,
            _ => throw new BacktestValidationException($"Unknown metric '{metric}'."),
        };
    }

    /// <summary>O1: the values Min + k * Step up to Max (inclusive), rounded to 10 decimals.</summary>
    private static List<double> BuildAxis(SweepAxis axis, IStrategy strategy, string axisName)
    {
        ParameterDefinition? definition = strategy.Parameters.FirstOrDefault(p => p.Key == axis.ParameterKey);
        if (definition == null)
        {
            throw new BacktestValidationException(
                $"Axis {axisName}: unknown parameter '{axis.ParameterKey}' for strategy '{strategy.Name}'.");
        }

        if (strategy.Parameters.Count < 2)
        {
            throw new BacktestValidationException(
                $"Strategy '{strategy.Name}' needs at least 2 parameters to build a heatmap.");
        }

        // written so that NaN and infinity are rejected too
        if (!double.IsFinite(axis.Min) || !double.IsFinite(axis.Max) || !double.IsFinite(axis.Step) || axis.Step <= 0)
        {
            throw new BacktestValidationException($"Axis {axisName}: the step must be greater than 0 and the bounds finite.");
        }

        if (axis.Min > axis.Max)
        {
            throw new BacktestValidationException($"Axis {axisName}: Min must not be greater than Max.");
        }

        string range = $"{definition.Min.ToString(CultureInfo.InvariantCulture)} and {definition.Max.ToString(CultureInfo.InvariantCulture)}";
        if (axis.Min < definition.Min || axis.Max > definition.Max)
        {
            throw new BacktestValidationException(
                $"Axis {axisName}: parameter '{definition.Key}' must stay between {range}.");
        }

        // count before building the list, so a tiny step cannot fill the memory
        double count = Math.Floor(((axis.Max - axis.Min) / axis.Step) + 1e-9) + 1;
        if (count > Conventions.MaxSweepCells)
        {
            throw new BacktestValidationException(
                $"Axis {axisName} has {count.ToString(CultureInfo.InvariantCulture)} values; the heatmap is limited to {Conventions.MaxSweepCells} cells.");
        }

        List<double> values = new();
        for (int k = 0; k < (int)count; k++)
        {
            values.Add(Math.Round(axis.Min + (k * axis.Step), 10));
        }

        if (definition.Type == ParameterType.Integer && values.Any(value => value != Math.Floor(value)))
        {
            throw new BacktestValidationException(
                $"Axis {axisName}: parameter '{definition.Key}' takes whole numbers, so Min and Step must be whole numbers.");
        }

        return values;
    }
}
