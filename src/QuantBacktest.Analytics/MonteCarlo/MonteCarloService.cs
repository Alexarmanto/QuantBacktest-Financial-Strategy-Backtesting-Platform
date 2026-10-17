using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Analytics;

// P2-X1: Monte Carlo simulation of the trades of a finished backtest (CONTRACT section 9, MC1 to MC7).
// The trades are drawn again and again at random, in a random order, to see what results luck could give.
internal class MonteCarloService : IMonteCarloService
{
    public MonteCarloResult Run(BacktestResult result, MonteCarloRequest request)
    {
        int tradeCount = result.Trades.Count;
        int simulations = request.Simulations;

        // MC1: enough trades and a sensible number of simulations
        if (tradeCount < Conventions.MinTradesForMonteCarlo)
        {
            throw new BacktestValidationException(
                $"Monte Carlo needs at least {Conventions.MinTradesForMonteCarlo} trades; this backtest has {tradeCount}.");
        }
        if (simulations < 100 || simulations > Conventions.MaxMonteCarloSimulations)
        {
            throw new BacktestValidationException(
                $"The number of simulations must be between 100 and {Conventions.MaxMonteCarloSimulations}.");
        }

        // the return of each real trade, in the real order
        double[] returns = new double[tradeCount];
        for (int j = 0; j < tradeCount; j++)
        {
            returns[j] = result.Trades[j].ReturnFraction;
        }

        // MC2: one random generator with the seed, each simulation draws "tradeCount" trades
        // (with replacement: the same trade can be drawn several times)
        Random random = new Random(request.Seed);
        double[] finalReturns = new double[simulations];
        double[] maxDrawdowns = new double[simulations];
        double[] path = new double[tradeCount];
        for (int s = 0; s < simulations; s++)
        {
            for (int j = 0; j < tradeCount; j++)
            {
                path[j] = returns[random.Next(tradeCount)];
            }

            // MC3
            finalReturns[s] = MonteCarloMath.FinalReturn(path);
            maxDrawdowns[s] = MonteCarloMath.MaxDrawdown(path);
        }

        // MC5: share of the simulations that end with a loss
        int losses = 0;
        for (int s = 0; s < simulations; s++)
        {
            if (finalReturns[s] < 0)
            {
                losses++;
            }
        }
        double probabilityOfLoss = (double)losses / simulations;

        // MC4, MC5, MC6 (MC7: the drawdown is measured trade by trade, not bar by bar like the engine)
        return new MonteCarloResult(
            request,
            tradeCount,
            MonteCarloMath.Percentiles(finalReturns),
            MonteCarloMath.Percentiles(maxDrawdowns),
            probabilityOfLoss,
            MonteCarloMath.FinalReturn(returns),
            MonteCarloMath.MaxDrawdown(returns),
            MonteCarloMath.Histogram(finalReturns, Conventions.HistogramBinCount),
            MonteCarloMath.Histogram(maxDrawdowns, Conventions.HistogramBinCount));
    }
}
