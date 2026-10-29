using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>
/// Simulates a strategy on a price series (docs/CONTRACT.md section 4, rules X1 to X15).
/// Pure: no network, no database, no file, no clock. Same inputs, same result.
/// </summary>
internal sealed class BacktestEngine : IBacktestEngine
{
    public BacktestResult Run(BacktestRequest request, PriceSeries series, IStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(strategy);

        ExecutionSettings settings = request.Execution;
        RequestValidator.ValidateExecution(settings);

        // X1: the bars of the period
        IReadOnlyList<PriceBar> allBars = series.Bars;
        FindPeriod(allBars, request.From, request.To, out int first, out int last);

        // X2: the strategy sees every bar up to the last one, including the warm-up bars before "From"
        List<PriceBar> history = allBars.Take(last + 1).ToList();
        IReadOnlyList<Signal> signals = strategy.GenerateSignals(history, request.Parameters);
        IReadOnlyList<IndicatorSeries> indicators = strategy.ComputeIndicators(history, request.Parameters);
        CheckStrategyOutput(strategy, history.Count, signals, indicators);

        // X7: the main loop
        SimulatedPortfolio portfolio = new(settings.InitialCapital, settings.FeeRate, settings.SlippageRate);
        int barCount = last - first + 1;
        List<PriceBar> bars = new(barCount);
        List<decimal> equity = new(barCount);
        List<bool> inPosition = new(barCount);
        Signal pending = Signal.Hold;
        decimal entryPeak = 0; // highest price since the entry, used by the trailing stop (X8)

        for (int i = first; i <= last; i++)
        {
            PriceBar bar = allBars[i];

            // (a) X4: the signal of the previous bar is executed at today's Open
            if (pending == Signal.Buy && !portfolio.IsInvested)
            {
                portfolio.Buy(bar.Date, i, bar.Open);
                entryPeak = portfolio.EntryPrice;
            }
            else if (pending == Signal.Sell && portfolio.IsInvested)
            {
                portfolio.Sell(bar.Date, bar.Open, ExitReason.Signal, i - portfolio.EntryIndex);
            }

            // (b) X8: stop-loss, take-profit and trailing stop
            if (portfolio.IsInvested)
            {
                if (FindProtectiveExit(bar, i, portfolio.EntryPrice, portfolio.EntryIndex, entryPeak, settings,
                        out decimal exitPrice, out ExitReason exitReason))
                {
                    portfolio.Sell(bar.Date, exitPrice, exitReason, i - portfolio.EntryIndex);
                }
                else
                {
                    // the High of this bar counts for the peak of the next bars
                    entryPeak = Math.Max(entryPeak, bar.High);
                }
            }

            // (c) X10: the position still open on the last bar is sold at the Close
            bool heldAtClose = portfolio.IsInvested;
            if (i == last && portfolio.IsInvested)
            {
                portfolio.Sell(bar.Date, bar.Close, ExitReason.EndOfData, i - portfolio.EntryIndex + 1);
            }

            // (d) X11: equity at the close
            bars.Add(bar);
            equity.Add(portfolio.Cash + portfolio.Quantity * bar.Close);
            inPosition.Add(heldAtClose);

            // X3 and X4: the signal of this bar will be executed tomorrow (never on the last bar)
            pending = i < last ? signals[i] : Signal.Hold;
        }

        List<DateOnly> dates = bars.Select(b => b.Date).ToList();

        // X14: benchmark
        List<decimal> benchmarkEquity = BuyAndHoldEquity(bars, settings);

        // metrics (section 5)
        PerformanceMetrics metrics = PerformanceCalculator.Compute(
            equity, dates, inPosition, settings.InitialCapital, portfolio.Trades, request.Timeframe, settings.RiskFreeRate);
        PerformanceMetrics benchmarkMetrics = PerformanceCalculator.ComputeBenchmark(
            benchmarkEquity, dates, settings.InitialCapital, request.Timeframe, settings.RiskFreeRate);

        // X15: drawdown of each point
        List<EquityPoint> points = new(barCount);
        decimal equityPeak = settings.InitialCapital;
        decimal benchmarkPeak = settings.InitialCapital;
        for (int k = 0; k < barCount; k++)
        {
            equityPeak = Math.Max(equityPeak, equity[k]);
            benchmarkPeak = Math.Max(benchmarkPeak, benchmarkEquity[k]);
            points.Add(new EquityPoint(
                dates[k],
                equity[k],
                benchmarkEquity[k],
                (double)((equityPeak - equity[k]) / equityPeak),
                (double)((benchmarkPeak - benchmarkEquity[k]) / benchmarkPeak),
                inPosition[k]));
        }

        // X2: keep only the part of each indicator that belongs to the period
        List<IndicatorSeries> slicedIndicators = new();
        foreach (IndicatorSeries indicator in indicators)
        {
            slicedIndicators.Add(indicator with { Values = indicator.Values.Skip(first).ToList() });
        }

        return new BacktestResult(
            Request: request,
            StrategyName: strategy.Name,
            PricesAdjusted: series.IsAdjusted,
            Bars: bars,
            Equity: points,
            Trades: portfolio.Trades.ToList(),
            Metrics: metrics,
            BenchmarkMetrics: benchmarkMetrics,
            Indicators: slicedIndicators);
    }

    /// <summary>
    /// X8 and X9. Looks for a stop or a target hit on this bar. Order: gap at the Open (stop, then target), then
    /// during the bar (stop, then target): the stop always comes first, the prudent assumption.
    /// Returns the reference price (before slippage) and the exit reason.
    /// </summary>
    private static bool FindProtectiveExit(
        PriceBar bar, int index, decimal entryPrice, int entryIndex, decimal peak, ExecutionSettings settings,
        out decimal exitPrice, out ExitReason reason)
    {
        exitPrice = 0;
        reason = ExitReason.Signal;

        // the stop level is the highest of the active levels
        decimal? stopLevel = null;
        ExitReason stopReason = ExitReason.StopLoss;
        if (settings.StopLossFraction != null)
        {
            stopLevel = entryPrice * (1 - settings.StopLossFraction.Value);
        }

        if (settings.TrailingStopFraction != null)
        {
            decimal trailingLevel = peak * (1 - settings.TrailingStopFraction.Value);
            if (stopLevel == null || trailingLevel > stopLevel.Value)
            {
                stopLevel = trailingLevel;
                stopReason = ExitReason.TrailingStop;
            }
        }

        decimal? target = null;
        if (settings.TakeProfitFraction != null)
        {
            target = entryPrice * (1 + settings.TakeProfitFraction.Value);
        }

        // gap at the Open: not possible on the bar of the entry, the Open is the entry price itself
        if (index > entryIndex)
        {
            if (stopLevel != null && bar.Open <= stopLevel.Value)
            {
                exitPrice = bar.Open;
                reason = stopReason;
                return true;
            }

            if (target != null && bar.Open >= target.Value)
            {
                exitPrice = bar.Open;
                reason = ExitReason.TakeProfit;
                return true;
            }
        }

        // during the bar
        if (stopLevel != null && bar.Low <= stopLevel.Value)
        {
            exitPrice = stopLevel.Value;
            reason = stopReason;
            return true;
        }

        if (target != null && bar.High >= target.Value)
        {
            exitPrice = target.Value;
            reason = ExitReason.TakeProfit;
            return true;
        }

        return false;
    }
    /// <summary>X1: first and last index of the bars with From &lt;= date &lt;= To (a null bound is open).</summary>
    private static void FindPeriod(IReadOnlyList<PriceBar> bars, DateOnly? from, DateOnly? to, out int first, out int last)
    {
        if (from != null && to != null && from.Value > to.Value)
        {
            throw new BacktestValidationException("The start date must not be after the end date.");
        }

        first = -1;
        last = -1;
        for (int i = 0; i < bars.Count; i++)
        {
            DateOnly date = bars[i].Date;
            if (from != null && date < from.Value)
            {
                continue;
            }

            if (to != null && date > to.Value)
            {
                break;
            }

            if (first < 0)
            {
                first = i;
            }

            last = i;
        }

        if (first < 0 || last - first + 1 < 2)
        {
            throw new BacktestValidationException("The selected period must contain at least 2 bars.");
        }
    }

    /// <summary>A strategy that returns the wrong number of values is a bug, not a user error.</summary>
    private static void CheckStrategyOutput(
        IStrategy strategy, int expectedCount, IReadOnlyList<Signal> signals, IReadOnlyList<IndicatorSeries> indicators)
    {
        if (signals.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategy.Id}' returned {signals.Count} signals for {expectedCount} bars.");
        }

        foreach (IndicatorSeries indicator in indicators)
        {
            if (indicator.Values.Count != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Strategy '{strategy.Id}' returned {indicator.Values.Count} values for indicator '{indicator.Key}' and {expectedCount} bars.");
            }
        }
    }

    /// <summary>X14: buy at the Open of the first bar (slippage and fee), value at each Close, never sell.</summary>
    private static List<decimal> BuyAndHoldEquity(List<PriceBar> bars, ExecutionSettings settings)
    {
        decimal fill = bars[0].Open * (1 + settings.SlippageRate);
        decimal quantity = settings.InitialCapital / (fill * (1 + settings.FeeRate));

        List<decimal> equity = new(bars.Count);
        foreach (PriceBar bar in bars)
        {
            equity.Add(quantity * bar.Close);
        }

        return equity;
    }
}
