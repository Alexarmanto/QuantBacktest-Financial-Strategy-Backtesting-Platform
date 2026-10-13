using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>
/// NOT AUTHORITATIVE. A simplified simulation (fixed stop-loss only, no take-profit, no trailing stop,
/// almost no validation) so the UI can be built before the real engine exists.
/// The real rules are docs/CONTRACT.md sections 4 and 5, implemented by Part 3. Do not copy this code.
/// </summary>
public sealed class FakeBacktestService(IMarketDataService marketData, IStrategyCatalog catalog) : IBacktestService
{
    public async Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var strategy = await catalog.FindAsync(request.StrategyId, cancellationToken).ConfigureAwait(false)
            ?? throw new BacktestValidationException($"Unknown strategy '{request.StrategyId}'.");
        if (!strategy.SupportedTimeframes.Contains(request.Timeframe))
        {
            throw new BacktestValidationException($"Strategy '{strategy.Name}' does not support the {request.Timeframe} timeframe.");
        }

        if (request.Execution.InitialCapital <= 0m)
        {
            throw new BacktestValidationException("Initial capital must be greater than 0.");
        }

        var series = await marketData
            .GetSeriesAsync(request.Symbol, request.Timeframe, false, cancellationToken)
            .ConfigureAwait(false);
        return FakeSimulation.Run(request, series, strategy);
    }
}

internal static class FakeSimulation
{
    public static BacktestResult Run(BacktestRequest request, PriceSeries series, IStrategy strategy)
    {
        var all = series.Bars;
        var first = -1;
        var last = -1;
        for (var i = 0; i < all.Count; i++)
        {
            var date = all[i].Date;
            if ((request.From is null || date >= request.From.Value) && (request.To is null || date <= request.To.Value))
            {
                if (first < 0)
                {
                    first = i;
                }

                last = i;
            }
        }

        if (first < 0 || last - first + 1 < 2)
        {
            throw new BacktestValidationException("Not enough bars in the selected period.");
        }

        var history = all.Take(last + 1).ToList();
        var signals = strategy.GenerateSignals(history, request.Parameters);
        var execution = request.Execution;
        var fee = execution.FeeRate;
        var slip = execution.SlippageRate;

        var cash = execution.InitialCapital;
        var quantity = 0m;
        var entryPrice = 0m;
        var entryFees = 0m;
        var entryIndex = -1;
        var trades = new List<Trade>();
        var equity = new List<EquityPoint>(last - first + 1);

        var benchmarkFill = all[first].Open * (1m + slip);
        var benchmarkQuantity = execution.InitialCapital / (benchmarkFill * (1m + fee));
        var peak = execution.InitialCapital;
        var benchmarkPeak = execution.InitialCapital;

        void ExitPosition(int index, decimal referencePrice, ExitReason reason, int holdingBars)
        {
            var fill = referencePrice * (1m - slip);
            var exitFees = quantity * fill * fee;
            var proceeds = (quantity * fill) - exitFees;
            var cost = (quantity * entryPrice) + entryFees;
            var net = proceeds - cost;
            trades.Add(new Trade(all[entryIndex].Date, entryPrice, all[index].Date, fill, quantity, entryFees + exitFees, net, (double)(net / cost), reason, holdingBars));
            cash += proceeds;
            quantity = 0m;
            entryIndex = -1;
        }

        for (var i = first; i <= last; i++)
        {
            var bar = all[i];
            if (i > first)
            {
                var signal = signals[i - 1];
                if (signal == Signal.Buy && quantity == 0m)
                {
                    var fill = bar.Open * (1m + slip);
                    quantity = cash / (fill * (1m + fee));
                    entryFees = quantity * fill * fee;
                    cash -= (quantity * fill) + entryFees;
                    entryPrice = fill;
                    entryIndex = i;
                }
                else if (signal == Signal.Sell && quantity > 0m)
                {
                    ExitPosition(i, bar.Open, ExitReason.Signal, i - entryIndex);
                }
            }

            if (quantity > 0m && execution.StopLossFraction is decimal stopLoss)
            {
                var level = entryPrice * (1m - stopLoss);
                if (bar.Low <= level)
                {
                    ExitPosition(i, level, ExitReason.StopLoss, i - entryIndex);
                }
            }

            var inPosition = quantity > 0m;
            if (i == last && quantity > 0m)
            {
                ExitPosition(i, bar.Close, ExitReason.EndOfData, i - entryIndex + 1);
            }

            var strategyEquity = cash + (quantity * bar.Close);
            var benchmarkEquity = benchmarkQuantity * bar.Close;
            peak = Math.Max(peak, strategyEquity);
            benchmarkPeak = Math.Max(benchmarkPeak, benchmarkEquity);
            equity.Add(new EquityPoint(
                bar.Date,
                strategyEquity,
                benchmarkEquity,
                (double)((peak - strategyEquity) / peak),
                (double)((benchmarkPeak - benchmarkEquity) / benchmarkPeak),
                inPosition));
        }

        var count = last - first + 1;
        var bars = all.Skip(first).Take(count).ToList();
        var indicators = strategy.ComputeIndicators(history, request.Parameters)
            .Select(s => s with { Values = s.Values.Skip(first).Take(count).ToList() })
            .ToList();

        var dates = equity.Select(e => e.Date).ToList();
        var exposure = equity.Count(e => e.InPosition) / (double)equity.Count;
        var metrics = FakeMetrics.Compute(
            equity.Select(e => e.StrategyEquity).ToList(), dates, execution.InitialCapital, trades, exposure, request.Timeframe, execution.RiskFreeRate);
        var benchmark = FakeMetrics.Compute(
            equity.Select(e => e.BenchmarkEquity).ToList(), dates, execution.InitialCapital, [], 1.0, request.Timeframe, execution.RiskFreeRate);
        benchmark = benchmark with
        {
            TradeCount = 1,
            WinRate = benchmark.FinalEquity > execution.InitialCapital ? 1.0 : 0.0,
            ProfitFactor = null,
        };

        return new BacktestResult(request, strategy.Name, series.IsAdjusted, bars, equity, trades, metrics, benchmark, indicators);
    }
}

internal static class FakeMetrics
{
    public static PerformanceMetrics Compute(
        IReadOnlyList<decimal> equity,
        IReadOnlyList<DateOnly> dates,
        decimal initialCapital,
        IReadOnlyList<Trade> trades,
        double exposure,
        Timeframe timeframe,
        double riskFreeRate)
    {
        var periods = Conventions.PeriodsPerYear(timeframe);
        var riskFreePerPeriod = riskFreeRate / periods;
        var returns = new List<double>();
        for (var i = 1; i < equity.Count; i++)
        {
            returns.Add((double)(equity[i] / equity[i - 1]) - 1.0);
        }

        var mean = returns.Count > 0 ? returns.Average() : 0.0;
        var std = returns.Count > 1 ? Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1)) : 0.0;
        var downside = returns.Count > 0
            ? Math.Sqrt(returns.Sum(r => Math.Pow(Math.Min(0.0, r - riskFreePerPeriod), 2)) / returns.Count)
            : 0.0;

        var final = equity[^1];
        var growth = (double)(final / initialCapital);
        var years = (dates[^1].DayNumber - dates[0].DayNumber) / 365.25;
        var annualized = years > 0 && growth > 0 ? Math.Pow(growth, 1.0 / years) - 1.0 : 0.0;

        var peak = initialCapital;
        var maxDrawdown = 0.0;
        foreach (var value in equity)
        {
            peak = Math.Max(peak, value);
            maxDrawdown = Math.Max(maxDrawdown, (double)((peak - value) / peak));
        }

        double? sharpe = returns.Count > 1 && std > 0 ? (mean - riskFreePerPeriod) / std * Math.Sqrt(periods) : null;
        double? sortino = returns.Count > 1 && downside > 0 ? (mean - riskFreePerPeriod) / downside * Math.Sqrt(periods) : null;
        double? calmar = maxDrawdown > 0 ? annualized / maxDrawdown : null;

        var wins = trades.Count(t => t.NetProfit > 0m);
        var grossProfit = trades.Where(t => t.NetProfit > 0m).Sum(t => t.NetProfit);
        var grossLoss = -trades.Where(t => t.NetProfit < 0m).Sum(t => t.NetProfit);
        double? profitFactor = grossLoss > 0m ? (double)(grossProfit / grossLoss) : null;
        var winRate = trades.Count > 0 ? (double)wins / trades.Count : 0.0;

        return new PerformanceMetrics(
            final,
            growth - 1.0,
            annualized,
            mean,
            std * Math.Sqrt(periods),
            sharpe,
            sortino,
            calmar,
            maxDrawdown,
            trades.Count,
            winRate,
            profitFactor,
            exposure);
    }
}
