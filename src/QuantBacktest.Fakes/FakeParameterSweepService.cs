using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>
/// NOT AUTHORITATIVE. Returns a smooth synthetic surface instead of running backtests, so the heatmap page
/// and the train/test page can be built early. Real rules: docs/CONTRACT.md section 6 (Part 3).
/// </summary>
public sealed class FakeParameterSweepService(IStrategyCatalog catalog) : IParameterSweepService
{
    public async Task<SweepResult> RunAsync(SweepRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var strategy = await catalog.FindAsync(request.BaseRequest.StrategyId, cancellationToken).ConfigureAwait(false)
            ?? throw new BacktestValidationException($"Unknown strategy '{request.BaseRequest.StrategyId}'.");
        if (strategy.Parameters.Count < 2)
        {
            throw new BacktestValidationException("A heatmap needs a strategy with at least 2 parameters.");
        }

        if (request.X.ParameterKey == request.Y.ParameterKey)
        {
            throw new BacktestValidationException("The two axes must use different parameters.");
        }

        var xs = Axis(request.X);
        var ys = Axis(request.Y);
        if (xs.Count * ys.Count > Conventions.MaxSweepCells)
        {
            throw new BacktestValidationException($"Too many cells ({xs.Count * ys.Count}); the maximum is {Conventions.MaxSweepCells}.");
        }

        var cells = new List<SweepCell>(xs.Count * ys.Count);
        foreach (var y in ys)
        {
            foreach (var x in xs)
            {
                var value = Math.Round(Math.Sin(x / 7.0) + Math.Cos(y / 9.0), 4);
                cells.Add(new SweepCell(x, y, value, 5 + (int)(Math.Abs(x + y) % 20), null));
            }
        }

        var best = cells
            .Where(c => c.Value is not null)
            .OrderByDescending(c => c.Value)
            .ThenBy(c => c.Y)
            .ThenBy(c => c.X)
            .FirstOrDefault();
        return new SweepResult(request, xs, ys, cells, best, best?.Value * 0.8, IsIsolatedPeak: false);
    }

    private static List<double> Axis(SweepAxis axis)
    {
        if (axis.Step <= 0 || axis.Min > axis.Max)
        {
            throw new BacktestValidationException($"Invalid range for '{axis.ParameterKey}'.");
        }

        var values = new List<double>();
        for (var k = 0; ; k++)
        {
            var value = Math.Round(axis.Min + (k * axis.Step), 10);
            if (value > axis.Max + 1e-9 || values.Count > Conventions.MaxSweepCells)
            {
                break;
            }

            values.Add(value);
        }

        return values;
    }
}
