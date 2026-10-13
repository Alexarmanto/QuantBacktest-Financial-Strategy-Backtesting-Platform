using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>
/// NOT AUTHORITATIVE. Builds plausible-looking distributions around the backtest metrics without any resampling.
/// Real method: docs/CONTRACT.md section 9 (Part 2, Analytics).
/// </summary>
public sealed class FakeMonteCarloService : IMonteCarloService
{
    public MonteCarloResult Run(BacktestResult result, MonteCarloRequest request)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(request);
        if (result.Trades.Count < Conventions.MinTradesForMonteCarlo)
        {
            throw new BacktestValidationException(
                $"Monte Carlo needs at least {Conventions.MinTradesForMonteCarlo} trades; this backtest has {result.Trades.Count}.");
        }

        var finalReturn = result.Metrics.CumulativeReturn;
        var drawdown = result.Metrics.MaxDrawdown;
        var returns = new PercentileSummary(finalReturn - 0.30, finalReturn - 0.12, finalReturn, finalReturn + 0.10, finalReturn + 0.25);
        var drawdowns = new PercentileSummary(drawdown * 0.5, drawdown * 0.8, drawdown, drawdown * 1.3, drawdown * 1.8);

        return new MonteCarloResult(
            request,
            result.Trades.Count,
            returns,
            drawdowns,
            ProbabilityOfLoss: 0.18,
            ActualFinalReturn: finalReturn,
            ActualMaxDrawdown: drawdown,
            Histogram(returns.P5 - 0.1, returns.P95 + 0.1, request.Simulations),
            Histogram(0.0, Math.Max(0.01, drawdowns.P95 + 0.05), request.Simulations));
    }

    private static List<HistogramBin> Histogram(double min, double max, int total)
    {
        var bins = new List<HistogramBin>(Conventions.HistogramBinCount);
        var width = (max - min) / Conventions.HistogramBinCount;
        var middle = (Conventions.HistogramBinCount - 1) / 2.0;
        var weights = Enumerable.Range(0, Conventions.HistogramBinCount)
            .Select(b => Math.Exp(-Math.Pow((b - middle) / 4.0, 2)))
            .ToList();
        var sum = weights.Sum();
        for (var b = 0; b < Conventions.HistogramBinCount; b++)
        {
            bins.Add(new HistogramBin(min + (b * width), min + ((b + 1) * width), (int)Math.Round(total * weights[b] / sum)));
        }

        return bins;
    }
}
