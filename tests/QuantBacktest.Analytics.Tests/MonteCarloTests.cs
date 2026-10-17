using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Analytics.Tests;

// Monte Carlo simulation (P2-X1, CONTRACT section 9, PART-2 section 7 "Monte Carlo")
public class MonteCarloTests
{
    private const double Tolerance = 1e-12;
    private readonly MonteCarloService _service = new MonteCarloService();

    [Fact]
    public void Percentile_OneToFive() // MC4
    {
        double[] sorted = new double[] { 1, 2, 3, 4, 5 };

        // P5: h = 0.05 x 4 = 0.2 -> 1 + 0.2 x (2 - 1) = 1.2
        MonteCarloMath.Percentile(sorted, 0.05).Should().BeApproximately(1.2, Tolerance);
        MonteCarloMath.Percentile(sorted, 0.25).Should().BeApproximately(2, Tolerance);
        MonteCarloMath.Percentile(sorted, 0.5).Should().BeApproximately(3, Tolerance);
    }

    [Fact]
    public void Percentiles_SortTheValuesFirst() // MC4
    {
        PercentileSummary summary = MonteCarloMath.Percentiles(new double[] { 5, 1, 4, 2, 3 });

        summary.P5.Should().BeApproximately(1.2, Tolerance);
        summary.P50.Should().BeApproximately(3, Tolerance);
        summary.P75.Should().BeApproximately(4, Tolerance);
        summary.P95.Should().BeApproximately(4.8, Tolerance);
    }

    [Fact]
    public void Path_PlusTenThenMinusTen() // MC3
    {
        // V = 1.1 then 0.99: final return -1 %, largest fall 0.11 / 1.1 = 10 %
        double[] returns = new double[] { 0.1, -0.1 };

        MonteCarloMath.FinalReturn(returns).Should().BeApproximately(-0.01, Tolerance);
        MonteCarloMath.MaxDrawdown(returns).Should().BeApproximately(0.1, Tolerance);
    }

    [Fact]
    public void Run_ActualValues_UseTheRealOrderOfTheTrades() // MC3, MC5
    {
        // +10 %, -10 % repeated 3 times: V = 1.1, 0.99, 1.089, 0.9801, 1.07811, 0.970299
        BacktestResult result = ResultWithTrades(0.1, -0.1, 0.1, -0.1, 0.1, -0.1);

        MonteCarloResult monteCarlo = _service.Run(result, new MonteCarloRequest(1000, 42));

        monteCarlo.TradeCount.Should().Be(6);
        monteCarlo.ActualFinalReturn.Should().BeApproximately(0.970299 - 1, 1e-9);
        monteCarlo.ActualMaxDrawdown.Should().BeApproximately((1.1 - 0.970299) / 1.1, 1e-9);
    }

    [Fact]
    public void AllTradesWinning_NoLossIsPossible() // MC5
    {
        BacktestResult result = ResultWithTrades(0.02, 0.05, 0.01, 0.03, 0.04);

        MonteCarloResult monteCarlo = _service.Run(result, new MonteCarloRequest(1000, 42));

        monteCarlo.ProbabilityOfLoss.Should().Be(0);
        monteCarlo.MaxDrawdown.P95.Should().Be(0);
    }

    [Fact]
    public void SameSeed_SameResult_OtherSeed_OtherResult() // MC2
    {
        BacktestResult result = ResultWithTrades(0.05, -0.03, 0.08, -0.06, 0.02, 0.04, -0.01);

        MonteCarloResult first = _service.Run(result, new MonteCarloRequest(500, 7));
        MonteCarloResult again = _service.Run(result, new MonteCarloRequest(500, 7));
        MonteCarloResult other = _service.Run(result, new MonteCarloRequest(500, 8));

        again.FinalReturn.Should().Be(first.FinalReturn);
        again.MaxDrawdown.Should().Be(first.MaxDrawdown);
        again.ProbabilityOfLoss.Should().Be(first.ProbabilityOfLoss);
        again.FinalReturnHistogram.Should().Equal(first.FinalReturnHistogram);
        other.FinalReturn.Should().NotBe(first.FinalReturn);
    }

    [Fact]
    public void Histograms_CountEverySimulation_InTwentyBins() // MC6
    {
        BacktestResult result = ResultWithTrades(0.05, -0.03, 0.08, -0.06, 0.02, 0.04, -0.01);

        MonteCarloResult monteCarlo = _service.Run(result, new MonteCarloRequest(1000, 42));

        CountAll(monteCarlo.FinalReturnHistogram).Should().Be(1000);
        CountAll(monteCarlo.MaxDrawdownHistogram).Should().Be(1000);
        monteCarlo.FinalReturnHistogram.Count.Should().Be(20);
        for (int b = 1; b < monteCarlo.FinalReturnHistogram.Count; b++)
        {
            // each interval starts where the previous one ends
            monteCarlo.FinalReturnHistogram[b].Lower.Should().BeApproximately(monteCarlo.FinalReturnHistogram[b - 1].Upper, 1e-12);
        }
    }

    [Fact]
    public void Histogram_LastBinIncludesTheMax() // MC6
    {
        // 2 intervals of width 2: [0, 2) has 0 and 1, [2, 4] has 2, 3 and 4
        HistogramBin[] bins = MonteCarloMath.Histogram(new double[] { 0, 1, 2, 3, 4 }, 2);

        bins.Length.Should().Be(2);
        bins[0].Count.Should().Be(2);
        bins[1].Count.Should().Be(3);
        bins[1].Upper.Should().Be(4);
    }

    [Fact]
    public void AllTradesEqual_GivesOneSingleBin() // MC6
    {
        // every simulation gives the same result, so min = max
        BacktestResult result = ResultWithTrades(0.05, 0.05, 0.05, 0.05, 0.05);

        MonteCarloResult monteCarlo = _service.Run(result, new MonteCarloRequest(200, 42));

        monteCarlo.FinalReturnHistogram.Count.Should().Be(1);
        monteCarlo.FinalReturnHistogram[0].Count.Should().Be(200);
        monteCarlo.FinalReturnHistogram[0].Lower.Should().Be(monteCarlo.FinalReturnHistogram[0].Upper);
        monteCarlo.MaxDrawdownHistogram.Count.Should().Be(1);
    }

    [Theory]
    [InlineData(4, 1000)]       // fewer than 5 trades
    [InlineData(10, 99)]        // fewer than 100 simulations
    [InlineData(10, 10001)]     // more than 10 000 simulations
    public void NotEnoughTradesOrWrongSimulations_ThrowsBacktestValidationException(int tradeCount, int simulations) // MC1
    {
        double[] returns = new double[tradeCount];
        for (int j = 0; j < tradeCount; j++)
        {
            returns[j] = 0.01;
        }
        BacktestResult result = ResultWithTrades(returns);

        bool thrown = false;
        try
        {
            _service.Run(result, new MonteCarloRequest(simulations, 42));
        }
        catch (BacktestValidationException)
        {
            thrown = true;
        }

        thrown.Should().BeTrue();
    }

    [Fact]
    public void AddQuantBacktestAnalytics_RegistersTheServiceAsSingleton() // CONTRACT section 2
    {
        ServiceCollection services = new ServiceCollection();

        services.AddQuantBacktestAnalytics();

        bool found = false;
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IMonteCarloService))
            {
                found = true;
                descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
                descriptor.ImplementationType.Should().Be(typeof(MonteCarloService));
            }
        }
        found.Should().BeTrue();
    }

    // ---- helpers ----

    private static int CountAll(IReadOnlyList<HistogramBin> bins)
    {
        int total = 0;
        foreach (HistogramBin bin in bins)
        {
            total = total + bin.Count;
        }
        return total;
    }

    // a backtest result with only the trades filled (the Monte Carlo only reads ReturnFraction)
    private static BacktestResult ResultWithTrades(params double[] returns)
    {
        List<Trade> trades = new List<Trade>();
        DateOnly date = new DateOnly(2026, 1, 5);
        for (int j = 0; j < returns.Length; j++)
        {
            trades.Add(new Trade(date, 100m, date.AddDays(1), 100m, 1m, 0m, 0m, returns[j], ExitReason.Signal, 1));
            date = date.AddDays(2);
        }

        BacktestRequest request = new BacktestRequest("TEST", Timeframe.Daily, "sma-crossover", new Dictionary<string, double>(), null, null, ExecutionSettings.Default);
        PerformanceMetrics metrics = new PerformanceMetrics(10000m, 0, 0, 0, 0, null, null, null, 0, trades.Count, 0, null, 0);

        return new BacktestResult(request, "Test", true, new PriceBar[0], new EquityPoint[0], trades, metrics, metrics, new IndicatorSeries[0]);
    }
}
