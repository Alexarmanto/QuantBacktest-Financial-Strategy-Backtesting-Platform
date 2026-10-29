using QuantBacktest.Core.Models;

namespace QuantBacktest.Engine.Tests;

public class PerformanceCalculatorTests
{
    private const double Tolerance = 1e-6;

    // Reference case of the spec: equities [100, 110, 99, 108.9], capital 100, daily, risk-free rate 0.
    private static readonly decimal[] ReferenceEquity = [100m, 110m, 99m, 108.9m];

    private static DateOnly[] Dates(int count, DateOnly? start = null)
    {
        var dates = new DateOnly[count];
        var current = start ?? new DateOnly(2024, 1, 1);
        for (var i = 0; i < count; i++)
        {
            dates[i] = current;
            current = current.AddDays(1);
        }

        return dates;
    }

    private static bool[] Flags(int count, bool value) => Enumerable.Repeat(value, count).ToArray();

    private static Trade TradeWithProfit(decimal netProfit) => new(
        EntryDate: new DateOnly(2024, 1, 2),
        EntryPrice: 100m,
        ExitDate: new DateOnly(2024, 1, 3),
        ExitPrice: 100m,
        Quantity: 1m,
        Fees: 0m,
        NetProfit: netProfit,
        ReturnFraction: 0,
        ExitReason: ExitReason.Signal,
        HoldingBars: 1);

    private static PerformanceMetrics Compute(
        IReadOnlyList<decimal> equity,
        decimal initialCapital = 100m,
        IReadOnlyList<Trade>? trades = null,
        Timeframe timeframe = Timeframe.Daily,
        double riskFreeRate = 0,
        IReadOnlyList<bool>? inPosition = null,
        IReadOnlyList<DateOnly>? dates = null) =>
        PerformanceCalculator.Compute(
            equity,
            dates ?? Dates(equity.Count),
            inPosition ?? Flags(equity.Count, false),
            initialCapital,
            trades ?? [],
            timeframe,
            riskFreeRate);

    // ---- M1, M2 ----

    [Fact]
    public void M1_M2_FinalEquity_And_CumulativeReturn()
    {
        var metrics = Compute(ReferenceEquity);

        // M1: FinalEquity = E_{n-1}
        metrics.FinalEquity.Should().Be(108.9m);
        // M2: FinalEquity / I - 1 = 108.9 / 100 - 1
        metrics.CumulativeReturn.Should().BeApproximately(0.089, Tolerance);
    }

    // ---- M3 ----

    [Fact]
    public void M3_AnnualizedReturn_UsesCalendarYears()
    {
        var dates = new[] { new DateOnly(2021, 1, 1), new DateOnly(2023, 1, 1) };

        var metrics = Compute([10_000m, 12_100m], 10_000m, dates: dates);

        // (FinalEquity / I)^(1 / years) - 1, years = 730 days / 365.25
        var years = 730 / 365.25;
        var expected = Math.Pow(12_100.0 / 10_000.0, 1 / years) - 1;
        metrics.AnnualizedReturn.Should().BeApproximately(expected, Tolerance);
        metrics.AnnualizedReturn.Should().BeApproximately(0.100072, 1e-5);
    }

    [Fact]
    public void M3_AnnualizedReturn_IsZero_WhenPeriodHasNoDuration()
    {
        var sameDay = new DateOnly(2024, 1, 1);

        var metrics = Compute([100m, 110m], dates: [sameDay, sameDay]);

        metrics.AnnualizedReturn.Should().Be(0);
    }

    [Fact]
    public void M3_AnnualizedReturn_IsZero_WhenFinalEquityIsNotPositive()
    {
        var metrics = Compute([100m, 50m, 0m]);

        metrics.AnnualizedReturn.Should().Be(0);
    }

    [Fact]
    public void M3_M14_AnnualizedReturn_IsZero_WhenResultOverflows()
    {
        var dates = new[] { new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2) };

        // 1 day, x1e20 gain: (1e20)^(365.25) overflows a double.
        var metrics = Compute([1m, 100_000_000_000_000_000_000m], 1m, dates: dates);

        metrics.AnnualizedReturn.Should().Be(0);
    }

    // ---- M4 ----

    [Fact]
    public void M4_AverageReturnPerPeriod_IsMeanOfReturns()
    {
        var metrics = Compute(ReferenceEquity);

        // r = [+0.1, -0.1, +0.1]; mean = 0.1 / 3
        metrics.AverageReturnPerPeriod.Should().BeApproximately(0.1 / 3, Tolerance);
    }

    [Fact]
    public void M4_AverageReturnPerPeriod_IsZero_WithSinglePoint()
    {
        Compute([100m]).AverageReturnPerPeriod.Should().Be(0);
    }

    // ---- M5 ----

    [Fact]
    public void M5_Volatility_IsSampleStdDevTimesSqrtPeriods()
    {
        var metrics = Compute(ReferenceEquity);

        // sample std = sqrt(sum((r - mean)^2) / (n - 1)), n = 3 returns; annualised by sqrt(252)
        double[] r = [0.1, -0.1, 0.1];
        var mean = r.Sum() / 3;
        var std = Math.Sqrt(r.Sum(x => (x - mean) * (x - mean)) / 2);
        metrics.AnnualizedVolatility.Should().BeApproximately(std * Math.Sqrt(252), Tolerance);
        metrics.AnnualizedVolatility.Should().BeApproximately(1.83303, 1e-5);
    }

    [Fact]
    public void M5_Volatility_IsZero_WithFewerThanTwoReturns()
    {
        Compute([100m, 110m]).AnnualizedVolatility.Should().Be(0);
        Compute([100m]).AnnualizedVolatility.Should().Be(0);
    }

    [Fact]
    public void M5_Volatility_UsesSqrt52_ForWeekly()
    {
        var metrics = Compute(ReferenceEquity, timeframe: Timeframe.Weekly);

        double[] r = [0.1, -0.1, 0.1];
        var mean = r.Sum() / 3;
        var std = Math.Sqrt(r.Sum(x => (x - mean) * (x - mean)) / 2);
        metrics.AnnualizedVolatility.Should().BeApproximately(std * Math.Sqrt(52), Tolerance);
    }

    // ---- M6 ----

    [Fact]
    public void M6_Sharpe_ReferenceCase()
    {
        var metrics = Compute(ReferenceEquity);

        // (mean - rf_p) / std * sqrt(P), rf = 0
        double[] r = [0.1, -0.1, 0.1];
        var mean = r.Sum() / 3;
        var std = Math.Sqrt(r.Sum(x => (x - mean) * (x - mean)) / 2);
        metrics.SharpeRatio.Should().BeApproximately(mean / std * Math.Sqrt(252), Tolerance);
        metrics.SharpeRatio.Should().BeApproximately(4.58258, 1e-5);
    }

    [Fact]
    public void M6_Sharpe_UsesSqrt52_ForWeekly()
    {
        var metrics = Compute(ReferenceEquity, timeframe: Timeframe.Weekly);

        double[] r = [0.1, -0.1, 0.1];
        var mean = r.Sum() / 3;
        var std = Math.Sqrt(r.Sum(x => (x - mean) * (x - mean)) / 2);
        metrics.SharpeRatio.Should().BeApproximately(mean / std * Math.Sqrt(52), Tolerance);
    }

    [Fact]
    public void M6_Sharpe_DecreasesWithRiskFreeRate()
    {
        var withoutRf = Compute(ReferenceEquity);
        var withRf = Compute(ReferenceEquity, riskFreeRate: 0.02);

        // rf_p = 0.02 / 252
        double[] r = [0.1, -0.1, 0.1];
        var mean = r.Sum() / 3;
        var std = Math.Sqrt(r.Sum(x => (x - mean) * (x - mean)) / 2);
        var rfPeriod = 0.02 / 252;
        withRf.SharpeRatio.Should().BeApproximately((mean - rfPeriod) / std * Math.Sqrt(252), Tolerance);
        withRf.SharpeRatio.Should().BeLessThan(withoutRf.SharpeRatio!.Value);
    }

    [Fact]
    public void M6_Sharpe_IsNull_WhenFewerThanTwoReturns()
    {
        Compute([100m, 110m]).SharpeRatio.Should().BeNull();
        Compute([100m]).SharpeRatio.Should().BeNull();
    }

    [Fact]
    public void M6_Sharpe_IsNull_WhenReturnsAreConstant()
    {
        // constant +10 % per bar: zero standard deviation
        var metrics = Compute([100m, 110m, 121m, 133.1m]);

        metrics.SharpeRatio.Should().BeNull();
    }

    [Fact]
    public void M6_Sharpe_IsNull_WhenEquityIsFlat()
    {
        Compute([100m, 100m, 100m, 100m]).SharpeRatio.Should().BeNull();
    }

    // ---- M7 ----

    [Fact]
    public void M7_Sortino_ReferenceCase()
    {
        var metrics = Compute(ReferenceEquity);

        // DD = sqrt(sum(min(0, r - rf_p)^2) / n) = sqrt(0.01 / 3); Sortino = (mean - rf_p) / DD * sqrt(P)
        double[] r = [0.1, -0.1, 0.1];
        var mean = r.Sum() / 3;
        var downside = Math.Sqrt(r.Sum(x => Math.Min(0, x) * Math.Min(0, x)) / 3);
        metrics.SortinoRatio.Should().BeApproximately(mean / downside * Math.Sqrt(252), Tolerance);
        metrics.SortinoRatio.Should().BeApproximately(9.16515, 1e-5);
    }

    [Fact]
    public void M7_Sortino_DecreasesWithRiskFreeRate()
    {
        var withoutRf = Compute(ReferenceEquity);
        var withRf = Compute(ReferenceEquity, riskFreeRate: 0.02);

        double[] r = [0.1, -0.1, 0.1];
        var rfPeriod = 0.02 / 252;
        var mean = r.Sum() / 3;
        var downside = Math.Sqrt(r.Sum(x => Math.Min(0, x - rfPeriod) * Math.Min(0, x - rfPeriod)) / 3);
        withRf.SortinoRatio.Should().BeApproximately((mean - rfPeriod) / downside * Math.Sqrt(252), Tolerance);
        withRf.SortinoRatio.Should().BeLessThan(withoutRf.SortinoRatio!.Value);
    }

    [Fact]
    public void M7_Sortino_IsNull_WhenThereIsNoDownside()
    {
        // only positive returns: DD = 0
        Compute([100m, 101m, 103m, 106m]).SortinoRatio.Should().BeNull();
    }

    [Fact]
    public void M7_Sortino_IsNull_WhenFewerThanTwoReturns()
    {
        Compute([100m, 90m]).SortinoRatio.Should().BeNull();
        Compute([100m]).SortinoRatio.Should().BeNull();
    }

    // ---- M8 ----

    [Fact]
    public void M8_MaxDrawdown_ReferenceCase()
    {
        // peak 110, trough 99: (110 - 99) / 110 = 0.10
        Compute(ReferenceEquity).MaxDrawdown.Should().BeApproximately(0.10, Tolerance);
    }

    [Fact]
    public void M8_MaxDrawdown_StartsFromInitialCapitalAsPeak()
    {
        // peak starts at I = 100, equity never exceeds it: (100 - 80) / 100 = 0.20
        Compute([95m, 80m, 90m]).MaxDrawdown.Should().BeApproximately(0.20, Tolerance);
    }

    [Fact]
    public void M8_MaxDrawdown_IsZero_WhenEquityOnlyRises()
    {
        Compute([100m, 105m, 110m]).MaxDrawdown.Should().Be(0);
    }

    // ---- M9 ----

    [Fact]
    public void M9_Calmar_IsAnnualizedReturnOverMaxDrawdown()
    {
        var metrics = Compute(ReferenceEquity);

        // AnnualizedReturn / MaxDrawdown; 3 days between first and last date
        var years = 3 / 365.25;
        var annualized = Math.Pow(108.9 / 100.0, 1 / years) - 1;
        metrics.CalmarRatio.Should().BeApproximately(annualized / 0.10, annualized * 1e-6);
    }

    [Fact]
    public void M9_Calmar_IsNull_WhenMaxDrawdownIsZero()
    {
        Compute([100m, 105m, 110m]).CalmarRatio.Should().BeNull();
    }

    // ---- M10 ----

    [Fact]
    public void M10_TradeCount_And_WinRate()
    {
        Trade[] trades = [TradeWithProfit(30m), TradeWithProfit(-10m), TradeWithProfit(20m), TradeWithProfit(-5m)];

        var metrics = Compute(ReferenceEquity, trades: trades);

        metrics.TradeCount.Should().Be(4);
        metrics.WinRate.Should().BeApproximately(0.5, Tolerance);
    }

    [Fact]
    public void M10_WinRate_CountsOnlyStrictlyPositiveProfits()
    {
        Trade[] trades = [TradeWithProfit(0m), TradeWithProfit(10m)];

        Compute(ReferenceEquity, trades: trades).WinRate.Should().BeApproximately(0.5, Tolerance);
    }

    [Fact]
    public void M10_WinRate_IsZero_WithoutTrades()
    {
        var metrics = Compute(ReferenceEquity);

        metrics.TradeCount.Should().Be(0);
        metrics.WinRate.Should().Be(0);
    }

    // ---- M11 ----

    [Fact]
    public void M11_ProfitFactor_IsGrossGainsOverGrossLosses()
    {
        Trade[] trades = [TradeWithProfit(30m), TradeWithProfit(-10m), TradeWithProfit(20m), TradeWithProfit(-5m)];

        // (30 + 20) / |-10 + -5| = 50 / 15
        Compute(ReferenceEquity, trades: trades).ProfitFactor.Should().BeApproximately(50.0 / 15.0, Tolerance);
    }

    [Fact]
    public void M11_ProfitFactor_IsNull_WhenAllTradesWin()
    {
        Trade[] trades = [TradeWithProfit(30m), TradeWithProfit(20m)];

        Compute(ReferenceEquity, trades: trades).ProfitFactor.Should().BeNull();
    }

    [Fact]
    public void M11_ProfitFactor_IsNull_WithoutTrades()
    {
        Compute(ReferenceEquity).ProfitFactor.Should().BeNull();
    }

    [Fact]
    public void M11_ProfitFactor_IsZero_WhenAllTradesLose()
    {
        Trade[] trades = [TradeWithProfit(-10m), TradeWithProfit(-5m)];

        Compute(ReferenceEquity, trades: trades).ProfitFactor.Should().Be(0);
    }

    // ---- M12 ----

    [Fact]
    public void M12_Exposure_IsShareOfPointsInPosition()
    {
        bool[] inPosition = [false, true, true, false, false, false];

        var metrics = Compute([100m, 100m, 100m, 100m, 100m, 100m], inPosition: inPosition);

        // 2 / 6
        metrics.Exposure.Should().BeApproximately(2.0 / 6.0, Tolerance);
    }

    [Fact]
    public void M12_Exposure_IsZero_WhenNeverInPosition()
    {
        Compute(ReferenceEquity).Exposure.Should().Be(0);
    }

    // ---- M13 ----

    [Fact]
    public void M13_Benchmark_UsesSameFormulasAsStrategy()
    {
        var strategy = Compute(ReferenceEquity);

        var benchmark = PerformanceCalculator.ComputeBenchmark(
            ReferenceEquity, Dates(ReferenceEquity.Length), 100m, Timeframe.Daily, 0);

        benchmark.FinalEquity.Should().Be(strategy.FinalEquity);
        benchmark.CumulativeReturn.Should().BeApproximately(strategy.CumulativeReturn, Tolerance);
        benchmark.AnnualizedReturn.Should().BeApproximately(strategy.AnnualizedReturn, strategy.AnnualizedReturn * 1e-6);
        benchmark.AnnualizedVolatility.Should().BeApproximately(strategy.AnnualizedVolatility, Tolerance);
        benchmark.SharpeRatio.Should().BeApproximately(strategy.SharpeRatio!.Value, Tolerance);
        benchmark.SortinoRatio.Should().BeApproximately(strategy.SortinoRatio!.Value, Tolerance);
        benchmark.MaxDrawdown.Should().BeApproximately(strategy.MaxDrawdown, Tolerance);
    }

    [Fact]
    public void M13_Benchmark_HasFixedTradingFields_WhenItEndsAboveCapital()
    {
        var benchmark = PerformanceCalculator.ComputeBenchmark(
            ReferenceEquity, Dates(ReferenceEquity.Length), 100m, Timeframe.Daily, 0);

        benchmark.TradeCount.Should().Be(1);
        benchmark.WinRate.Should().Be(1);
        benchmark.ProfitFactor.Should().BeNull();
        benchmark.Exposure.Should().Be(1);
    }

    [Fact]
    public void M13_Benchmark_WinRateIsZero_WhenItEndsAtOrBelowCapital()
    {
        decimal[] equity = [100m, 90m, 100m];

        var benchmark = PerformanceCalculator.ComputeBenchmark(equity, Dates(3), 100m, Timeframe.Daily, 0);

        // FinalEquity = I is not a win
        benchmark.WinRate.Should().Be(0);
        benchmark.TradeCount.Should().Be(1);
        benchmark.Exposure.Should().Be(1);
    }

    // ---- M14: degenerate cases, no NaN, no infinity, no exception ----

    public static TheoryData<string, decimal[]> DegenerateEquities => new()
    {
        { "single point", [100m] },
        { "flat equity", [100m, 100m, 100m, 100m] },
        { "collapse to zero", [100m, 50m, 0m, 0m, 0m] },
        { "two points", [100m, 110m] },
        { "monotonic rise", [100m, 101m, 102m, 103m] },
    };

    [Theory]
    [MemberData(nameof(DegenerateEquities))]
    public void M14_DegenerateCases_NeverProduceNaNOrInfinity(string name, decimal[] equity)
    {
        _ = name;

        var metrics = Compute(equity);
        var benchmark = PerformanceCalculator.ComputeBenchmark(equity, Dates(equity.Length), 100m, Timeframe.Daily, 0);

        foreach (var m in new[] { metrics, benchmark })
        {
            double?[] values =
            [
                m.CumulativeReturn, m.AnnualizedReturn, m.AverageReturnPerPeriod, m.AnnualizedVolatility,
                m.SharpeRatio, m.SortinoRatio, m.CalmarRatio, m.MaxDrawdown, m.WinRate, m.ProfitFactor, m.Exposure,
            ];
            foreach (var value in values)
            {
                if (value is { } v)
                {
                    double.IsFinite(v).Should().BeTrue();
                }
            }

            m.MaxDrawdown.Should().BeInRange(0, 1);
            m.Exposure.Should().BeInRange(0, 1);
        }
    }

    [Fact]
    public void M14_FlatEquity_HasNoRatios()
    {
        var metrics = Compute([100m, 100m, 100m, 100m]);

        metrics.AnnualizedVolatility.Should().Be(0);
        metrics.SharpeRatio.Should().BeNull();
        metrics.SortinoRatio.Should().BeNull();
        metrics.CalmarRatio.Should().BeNull();
        metrics.MaxDrawdown.Should().Be(0);
        metrics.CumulativeReturn.Should().Be(0);
    }

    [Fact]
    public void M14_CollapseToZero_TreatsReturnAfterZeroEquityAsZero()
    {
        var metrics = Compute([100m, 50m, 0m, 0m]);

        // r = [-0.5, -1, 0 (previous equity is 0)]; total loss => drawdown 1
        metrics.MaxDrawdown.Should().Be(1);
        metrics.CumulativeReturn.Should().BeApproximately(-1, Tolerance);
        double[] r = [-0.5, -1.0, 0.0];
        metrics.AverageReturnPerPeriod.Should().BeApproximately(r.Sum() / 3, Tolerance);
    }

    // ---- Argument validation (programming errors, not user errors) ----

    [Fact]
    public void Compute_Throws_WhenEquityIsEmpty()
    {
        var act = () => PerformanceCalculator.Compute([], [], [], 100m, [], Timeframe.Daily, 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compute_Throws_WhenSeriesLengthsDiffer()
    {
        var act = () => PerformanceCalculator.Compute(
            ReferenceEquity, Dates(3), Flags(4, false), 100m, [], Timeframe.Daily, 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Compute_Throws_WhenInitialCapitalIsNotPositive()
    {
        var act = () => Compute(ReferenceEquity, initialCapital: 0m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
