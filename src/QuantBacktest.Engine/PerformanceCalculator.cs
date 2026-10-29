using QuantBacktest.Core;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine;

/// <summary>
/// Computes every performance metric of docs/CONTRACT.md section 5 (M1 to M14) from an equity curve.
/// Independent from the backtest engine: it only reads the numbers it is given. Pure and deterministic.
/// </summary>
internal static class PerformanceCalculator
{
    /// <summary>
    /// A standard deviation or downside deviation below this value is treated as zero, so floating-point noise
    /// on constant returns never turns into a huge ratio (M6, M7, M14).
    /// </summary>
    private const double Epsilon = 1e-12;

    private const double DaysPerYear = 365.25;

    /// <summary>Metrics of the strategy (M1 to M12, M14).</summary>
    /// <param name="equity">Equity at each close, E_0..E_{n-1}.</param>
    /// <param name="dates">Date of each equity point.</param>
    /// <param name="inPosition">Whether a position was held at each close (used for the exposure, M12).</param>
    /// <param name="initialCapital">I, strictly positive.</param>
    /// <param name="trades">Completed trades (used for M10 and M11).</param>
    /// <param name="timeframe">Gives the number of periods per year P.</param>
    /// <param name="riskFreeRate">Annual risk-free rate (rf_p = rate / P).</param>
    public static PerformanceMetrics Compute(
        IReadOnlyList<decimal> equity,
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<bool> inPosition,
        decimal initialCapital,
        IReadOnlyList<Trade> trades,
        Timeframe timeframe,
        double riskFreeRate)
    {
        ArgumentNullException.ThrowIfNull(trades);
        Validate(equity, dates, initialCapital);
        ArgumentNullException.ThrowIfNull(inPosition);
        if (inPosition.Count != equity.Count)
        {
            throw new ArgumentException("The position flags must have one value per equity point.", nameof(inPosition));
        }

        var exposure = inPosition.Count(held => held) / (double)inPosition.Count;

        // M10 and M11
        var winners = trades.Count(trade => trade.NetProfit > 0);
        var winRate = trades.Count == 0 ? 0 : winners / (double)trades.Count;
        var grossGains = trades.Where(trade => trade.NetProfit > 0).Sum(trade => trade.NetProfit);
        var losses = trades.Where(trade => trade.NetProfit < 0).ToList();
        double? profitFactor = losses.Count == 0
            ? null
            : Finite((double)grossGains / (double)Math.Abs(losses.Sum(trade => trade.NetProfit)));

        return Build(equity, dates, initialCapital, timeframe, riskFreeRate, trades.Count, winRate, profitFactor, exposure);
    }

    /// <summary>
    /// Metrics of the buy and hold benchmark (M13): same formulas on its equity curve, but the trading fields are
    /// fixed: one trade, a win only if it ends above the initial capital, no profit factor, always invested.
    /// </summary>
    public static PerformanceMetrics ComputeBenchmark(
        IReadOnlyList<decimal> equity,
        IReadOnlyList<DateOnly> dates,
        decimal initialCapital,
        Timeframe timeframe,
        double riskFreeRate)
    {
        Validate(equity, dates, initialCapital);

        var winRate = equity[^1] > initialCapital ? 1.0 : 0.0;
        return Build(equity, dates, initialCapital, timeframe, riskFreeRate, 1, winRate, null, 1.0);
    }

    private static PerformanceMetrics Build(
        IReadOnlyList<decimal> equity,
        IReadOnlyList<DateOnly> dates,
        decimal initialCapital,
        Timeframe timeframe,
        double riskFreeRate,
        int tradeCount,
        double winRate,
        double? profitFactor,
        double exposure)
    {
        var periodsPerYear = Conventions.PeriodsPerYear(timeframe);
        var sqrtPeriods = Math.Sqrt(periodsPerYear);
        var riskFreePerPeriod = riskFreeRate / periodsPerYear;

        var finalEquity = equity[^1];

        // M2
        var cumulativeReturn = Finite((double)(finalEquity / initialCapital - 1m)) ?? 0;

        // M3
        var annualizedReturn = AnnualizedReturn(finalEquity, initialCapital, dates[0], dates[^1]);

        // r_t = E_t / E_{t-1} - 1. A previous equity of 0 means the portfolio is wiped out, so nothing can change: r_t = 0.
        var returns = new double[equity.Count - 1];
        for (var t = 1; t < equity.Count; t++)
        {
            returns[t - 1] = equity[t - 1] == 0m ? 0 : (double)(equity[t] / equity[t - 1] - 1m);
        }

        // M4
        var mean = returns.Length == 0 ? 0 : returns.Average();

        // M5 and M6: sample standard deviation (n - 1)
        double volatility = 0;
        double? sharpe = null;
        if (returns.Length >= 2)
        {
            var std = Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (returns.Length - 1));
            volatility = Finite(std * sqrtPeriods) ?? 0;
            if (std > Epsilon)
            {
                sharpe = Finite((mean - riskFreePerPeriod) / std * sqrtPeriods);
            }
        }

        // M7: downside deviation against the per-period risk-free rate, over all n returns
        double? sortino = null;
        if (returns.Length >= 2)
        {
            var downside = Math.Sqrt(
                returns.Sum(r => Math.Min(0, r - riskFreePerPeriod) * Math.Min(0, r - riskFreePerPeriod)) / returns.Length);
            if (downside > Epsilon)
            {
                sortino = Finite((mean - riskFreePerPeriod) / downside * sqrtPeriods);
            }
        }

        // M8: the peak starts at the initial capital
        var maxDrawdown = MaxDrawdown(equity, initialCapital);

        // M9
        double? calmar = maxDrawdown == 0 ? null : Finite(annualizedReturn / maxDrawdown);

        return new PerformanceMetrics(
            FinalEquity: finalEquity,
            CumulativeReturn: cumulativeReturn,
            AnnualizedReturn: annualizedReturn,
            AverageReturnPerPeriod: Finite(mean) ?? 0,
            AnnualizedVolatility: volatility,
            SharpeRatio: sharpe,
            SortinoRatio: sortino,
            CalmarRatio: calmar,
            MaxDrawdown: maxDrawdown,
            TradeCount: tradeCount,
            WinRate: winRate,
            ProfitFactor: profitFactor,
            Exposure: exposure);
    }

    /// <summary>M3: (FinalEquity / I)^(1 / years) - 1, 0 when undefined or when the result is not a finite number (M14).</summary>
    private static double AnnualizedReturn(decimal finalEquity, decimal initialCapital, DateOnly first, DateOnly last)
    {
        var years = (last.DayNumber - first.DayNumber) / DaysPerYear;
        if (years <= 0 || finalEquity <= 0m)
        {
            return 0;
        }

        var growth = (double)(finalEquity / initialCapital);
        return Finite(Math.Pow(growth, 1 / years) - 1) ?? 0;
    }

    /// <summary>M8 and X15: largest (peak - equity) / peak, peak starting at the initial capital.</summary>
    private static double MaxDrawdown(IReadOnlyList<decimal> equity, decimal initialCapital)
    {
        var peak = initialCapital;
        var worst = 0.0;
        foreach (var value in equity)
        {
            if (value > peak)
            {
                peak = value;
            }

            var drawdown = (double)((peak - value) / peak);
            if (drawdown > worst)
            {
                worst = drawdown;
            }
        }

        return worst;
    }

    /// <summary>Returns the value, or null when it is NaN or infinite (M14: such a value must never leave the calculator).</summary>
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    private static void Validate(IReadOnlyList<decimal> equity, IReadOnlyList<DateOnly> dates, decimal initialCapital)
    {
        ArgumentNullException.ThrowIfNull(equity);
        ArgumentNullException.ThrowIfNull(dates);
        if (equity.Count == 0)
        {
            throw new ArgumentException("The equity curve must contain at least one point.", nameof(equity));
        }

        if (dates.Count != equity.Count)
        {
            throw new ArgumentException("The dates must have one value per equity point.", nameof(dates));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialCapital, 0m);
    }
}
