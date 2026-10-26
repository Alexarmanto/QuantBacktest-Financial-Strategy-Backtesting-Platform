using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Engine.Tests.TestSupport;
using QuantBacktest.Fakes;

namespace QuantBacktest.Engine.Tests;

/// <summary>O1, O2 and O5, and the loading of the data (once).</summary>
public class ParameterSweepServiceTests
{
    private static (ParameterSweepService service, StubMarketDataService data) CreateService(int barCount = 200)
    {
        PriceSeries series = SyntheticPrices.Generate("SYN", Timeframe.Daily, barCount, seed: 7);
        StubMarketDataService data = new(series);
        StubStrategyCatalog catalog = new(new TwoParameterStrategy());
        return (new ParameterSweepService(data, catalog, new BacktestEngine()), data);
    }

    private static SweepRequest Sweep(
        SweepAxis x, SweepAxis y, SweepMetric metric = SweepMetric.CumulativeReturn, Dictionary<string, double>? parameters = null)
    {
        BacktestRequest baseRequest = new(
            "SYN", Timeframe.Daily, "two-parameters", parameters ?? new Dictionary<string, double>(), null, null,
            ReferenceSeries.Settings());
        return new SweepRequest(baseRequest, x, y, metric);
    }

    private static SweepAxis Fast(double min, double max, double step) => new("fast", min, max, step);

    private static SweepAxis Slow(double min, double max, double step) => new("slow", min, max, step);

    // ---------- O1: axes ----------

    [Fact]
    public async Task IntegerAxis_5To25Step5_HasFiveValues() // O1
    {
        var (service, _) = CreateService();
        SweepResult result = await service.RunAsync(Sweep(Slow(5, 25, 5), Fast(2, 4, 1)));

        result.XValues.Should().Equal(5.0, 10.0, 15.0, 20.0, 25.0);
        result.YValues.Should().Equal(2.0, 3.0, 4.0);
    }

    [Fact]
    public async Task DecimalAxis_UsesRoundedValues() // O1: 0.01 + k * 0.01 must not give 0.020000000000000004
    {
        ParameterDefinition level = new("level", "Level", ParameterType.Decimal, 0.02, 0, 1, 0.01);
        ParameterDefinition other = new("other", "Other", ParameterType.Integer, 5, 2, 20, 1);
        IStrategy strategy = new DecimalStrategy(level, other);
        StubMarketDataService data = new(SyntheticPrices.Generate("SYN", barCount: 50));
        ParameterSweepService service = new(data, new StubStrategyCatalog(strategy), new BacktestEngine());
        BacktestRequest baseRequest = new(
            "SYN", Timeframe.Daily, "decimal", new Dictionary<string, double>(), null, null, ReferenceSeries.Settings());

        SweepResult result = await service.RunAsync(new SweepRequest(
            baseRequest, new SweepAxis("level", 0.01, 0.03, 0.01), new SweepAxis("other", 2, 3, 1), SweepMetric.CumulativeReturn));

        result.XValues.Should().Equal(0.01, 0.02, 0.03);
    }

    [Fact]
    public async Task StepNotWhole_OnAnIntegerParameter_IsRejected() // O1
    {
        var (service, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Sweep(Fast(2, 6, 0.5), Slow(5, 7, 1)));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("*whole numbers*");
    }

    [Fact]
    public async Task SameKeyOnBothAxes_IsRejected() // O1
    {
        var (service, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Sweep(Fast(2, 4, 1), Fast(2, 4, 1)));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("*two different parameters*");
    }

    [Fact]
    public async Task UnknownKey_IsRejected() // O1
    {
        var (service, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Sweep(new SweepAxis("nope", 1, 2, 1), Slow(5, 7, 1)));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("*unknown parameter 'nope'*");
    }

    [Fact]
    public async Task StepZeroOrNegative_IsRejected() // O1
    {
        var (service, _) = CreateService();

        Func<Task> zero = () => service.RunAsync(Sweep(Fast(2, 6, 0), Slow(5, 7, 1)));
        Func<Task> negative = () => service.RunAsync(Sweep(Fast(2, 6, -1), Slow(5, 7, 1)));

        await zero.Should().ThrowAsync<BacktestValidationException>();
        await negative.Should().ThrowAsync<BacktestValidationException>();
    }

    [Fact]
    public async Task MinGreaterThanMax_IsRejected() // O1
    {
        var (service, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Sweep(Fast(8, 4, 1), Slow(5, 7, 1)));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("*Min must not be greater than Max*");
    }

    [Fact]
    public async Task AxisOutsideTheParameterRange_IsRejected() // O1
    {
        var (service, _) = CreateService();

        // "fast" goes from 2 to 30
        Func<Task> run = () => service.RunAsync(Sweep(Fast(1, 6, 1), Slow(5, 7, 1)));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("*'fast' must stay between 2 and 30*");
    }

    [Fact]
    public async Task MoreThan625Cells_IsRejected() // O1
    {
        var (service, data) = CreateService();

        // 19 x 38 = 722 cells
        Func<Task> run = () => service.RunAsync(Sweep(Fast(2, 20, 1), Slow(3, 40, 1)));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("*722 cells*");
        data.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task StrategyWithLessThanTwoParameters_IsRejected() // O1
    {
        ScriptedStrategy single = new(
            new List<Signal>(),
            new List<ParameterDefinition> { new("fast", "Fast", ParameterType.Integer, 5, 2, 20, 1) });
        single.Id = "single";
        StubMarketDataService data = new(SyntheticPrices.Generate("SYN", barCount: 50));
        ParameterSweepService service = new(data, new StubStrategyCatalog(single), new BacktestEngine());
        BacktestRequest baseRequest = new(
            "SYN", Timeframe.Daily, "single", new Dictionary<string, double>(), null, null, ReferenceSeries.Settings());

        Func<Task> run = () => service.RunAsync(new SweepRequest(
            baseRequest, Fast(2, 4, 1), Fast(2, 4, 1), SweepMetric.CumulativeReturn));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("*at least 2 parameters*");
    }

    [Fact]
    public async Task UnknownStrategy_IsRejected()
    {
        var (service, _) = CreateService();
        SweepRequest request = Sweep(Fast(2, 4, 1), Slow(5, 7, 1));
        request = request with { BaseRequest = request.BaseRequest with { StrategyId = "nope" } };

        Func<Task> run = () => service.RunAsync(request);

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("Unknown strategy 'nope'.");
    }

    // ---------- O2: cells ----------

    [Fact]
    public async Task InvalidCombination_GivesAnEmptyCellWithTheError_AndTheGridContinues() // O2
    {
        var (service, _) = CreateService();

        // fast 2..6 (X) and slow 3..5 (Y): fast >= slow is refused by the strategy
        SweepResult result = await service.RunAsync(Sweep(Fast(2, 6, 2), Slow(3, 5, 1)));

        result.Cells.Should().HaveCount(9);
        SweepCell invalid = result.Cells.Single(cell => cell.X == 6 && cell.Y == 3);
        invalid.Value.Should().BeNull();
        invalid.TradeCount.Should().Be(0);
        invalid.Error.Should().Be("fast must be lower than slow.");

        SweepCell valid = result.Cells.Single(cell => cell.X == 2 && cell.Y == 5);
        valid.Error.Should().BeNull();
        valid.Value.Should().NotBeNull();
        valid.TradeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UndefinedMetric_GivesNullValueWithoutError() // O2
    {
        // a Sharpe ratio needs a non-zero volatility: with 3 bars and no trade it is undefined
        ScriptedStrategy a = new(
            new List<Signal>(),
            new List<ParameterDefinition>
            {
                new("p", "P", ParameterType.Integer, 1, 1, 3, 1),
                new("q", "Q", ParameterType.Integer, 1, 1, 3, 1),
            });
        a.Id = "flat";
        StubMarketDataService data = new(SyntheticPrices.Generate("SYN", barCount: 30));
        ParameterSweepService service = new(data, new StubStrategyCatalog(a), new BacktestEngine());
        BacktestRequest baseRequest = new(
            "SYN", Timeframe.Daily, "flat", new Dictionary<string, double>(), null, null, ReferenceSeries.Settings());

        SweepResult result = await service.RunAsync(new SweepRequest(
            baseRequest, new SweepAxis("p", 1, 2, 1), new SweepAxis("q", 1, 2, 1), SweepMetric.SharpeRatio));

        result.Cells.Should().OnlyContain(cell => cell.Value == null && cell.Error == null);
        result.Best.Should().BeNull();
    }

    [Fact]
    public async Task ASweptParameterInTheBaseRequest_IsReplacedInEveryCell()
    {
        var (service, _) = CreateService();

        // fast = 1000 in the base request is invalid, but "fast" is swept so the value is never used
        SweepResult result = await service.RunAsync(Sweep(
            Fast(2, 3, 1), Slow(10, 11, 1), parameters: new Dictionary<string, double> { ["fast"] = 1000 }));

        result.Cells.Should().OnlyContain(cell => cell.Error == null);
    }

    [Fact]
    public async Task OtherParametersOfTheBaseRequest_AreValidated()
    {
        var (service, _) = CreateService();

        // the strategy has only two parameters, both swept, so an unknown key is the way to test it
        Func<Task> run = () => service.RunAsync(Sweep(
            Fast(2, 3, 1), Slow(10, 11, 1), parameters: new Dictionary<string, double> { ["zzz"] = 1 }));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("Unknown parameter 'zzz'*");
    }

    [Fact]
    public async Task EachCell_MatchesAnIndependentBacktest() // O2: same simulation as a normal backtest
    {
        var (service, data) = CreateService();
        SweepResult result = await service.RunAsync(Sweep(Fast(2, 4, 2), Slow(6, 8, 2)));

        BacktestService backtest = new(data, new StubStrategyCatalog(new TwoParameterStrategy()), new BacktestEngine());
        foreach (SweepCell cell in result.Cells)
        {
            BacktestRequest request = result.Request.BaseRequest with
            {
                Parameters = new Dictionary<string, double> { ["fast"] = cell.X, ["slow"] = cell.Y },
            };
            BacktestResult single = await backtest.RunAsync(request);

            cell.Value.Should().Be(single.Metrics.CumulativeReturn);
            cell.TradeCount.Should().Be(single.Metrics.TradeCount);
        }
    }

    [Fact]
    public async Task DataIsLoadedOnlyOnce() // O2
    {
        var (service, data) = CreateService();

        await service.RunAsync(Sweep(Fast(2, 6, 2), Slow(10, 14, 2)));

        data.CallCount.Should().Be(1);
        data.LastForceRefresh.Should().BeFalse();
    }

    [Theory]
    [InlineData(SweepMetric.SharpeRatio)]
    [InlineData(SweepMetric.SortinoRatio)]
    [InlineData(SweepMetric.CalmarRatio)]
    [InlineData(SweepMetric.CumulativeReturn)]
    [InlineData(SweepMetric.AnnualizedReturn)]
    [InlineData(SweepMetric.ProfitFactor)]
    public async Task EveryMetric_ReadsTheMatchingField(SweepMetric metric)
    {
        var (service, data) = CreateService();
        SweepResult result = await service.RunAsync(Sweep(Fast(2, 2, 1), Slow(10, 10, 1), metric));

        BacktestService backtest = new(data, new StubStrategyCatalog(new TwoParameterStrategy()), new BacktestEngine());
        BacktestResult single = await backtest.RunAsync(result.Request.BaseRequest with
        {
            Parameters = new Dictionary<string, double> { ["fast"] = 2, ["slow"] = 10 },
        });
        PerformanceMetrics m = single.Metrics;
        double? expected = metric switch
        {
            SweepMetric.SharpeRatio => m.SharpeRatio,
            SweepMetric.SortinoRatio => m.SortinoRatio,
            SweepMetric.CalmarRatio => m.CalmarRatio,
            SweepMetric.CumulativeReturn => m.CumulativeReturn,
            SweepMetric.AnnualizedReturn => m.AnnualizedReturn,
            _ => m.ProfitFactor,
        };

        result.Cells.Should().ContainSingle().Which.Value.Should().Be(expected);
    }

    // ---------- O3 / O4 through the service ----------

    [Fact]
    public async Task BestCell_IsTheHighestValueOfTheGrid() // O3
    {
        var (service, _) = CreateService();

        SweepResult result = await service.RunAsync(Sweep(Fast(2, 6, 2), Slow(8, 12, 2)));

        double? highest = result.Cells.Where(cell => cell.Value != null).Max(cell => cell.Value);
        result.Best.Should().NotBeNull();
        result.Best!.Value.Should().Be(highest);
    }

    // ---------- O5: order, determinism, cancellation ----------

    [Fact]
    public async Task Cells_AreInRowMajorOrder() // O5
    {
        var (service, _) = CreateService();

        SweepResult result = await service.RunAsync(Sweep(Fast(2, 4, 1), Slow(8, 9, 1)));

        // for each Y (slow 8, 9) every X (fast 2, 3, 4)
        result.Cells.Select(cell => (cell.X, cell.Y)).Should().Equal(
            (2.0, 8.0), (3.0, 8.0), (4.0, 8.0), (2.0, 9.0), (3.0, 9.0), (4.0, 9.0));
    }

    [Fact]
    public async Task TwoCalls_GiveTheSameResult() // O5
    {
        var (service, _) = CreateService();
        SweepRequest request = Sweep(Fast(2, 6, 2), Slow(8, 12, 2));

        SweepResult first = await service.RunAsync(request);
        SweepResult second = await service.RunAsync(request);

        second.Cells.Should().Equal(first.Cells);
        second.Best.Should().Be(first.Best);
        second.IsIsolatedPeak.Should().Be(first.IsIsolatedPeak);
    }

    [Fact]
    public async Task Cancellation_IsRespected() // O5
    {
        var (service, _) = CreateService();
        using CancellationTokenSource source = new();
        source.Cancel();

        Func<Task> run = () => service.RunAsync(Sweep(Fast(2, 6, 2), Slow(8, 12, 2)), source.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MarketDataException_IsNotWrapped()
    {
        var (service, data) = CreateService();
        data.ExceptionToThrow = new MarketDataException(MarketDataErrorKind.NetworkError, "No network.");

        Func<Task> run = () => service.RunAsync(Sweep(Fast(2, 4, 1), Slow(8, 9, 1)));

        await run.Should().ThrowAsync<MarketDataException>().WithMessage("No network.");
    }

    // ---------- performance ----------

    [Fact]
    public async Task Grid625Cells_On1250Bars_RunsInLessThan10Seconds()
    {
        var (service, _) = CreateService(barCount: 1250);
        Stopwatch watch = Stopwatch.StartNew();

        // 25 values of fast (2..26) x 25 values of slow (30..54) = 625 cells
        SweepResult result = await service.RunAsync(Sweep(Fast(2, 26, 1), Slow(30, 54, 1)));

        watch.Stop();
        result.Cells.Should().HaveCount(625);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AddQuantBacktestEngine_RegistersTheSweepService()
    {
        ServiceCollection services = new();
        services.AddSingleton<IMarketDataService>(new StubMarketDataService(ReferenceSeries.Create()));
        services.AddSingleton<IStrategyCatalog>(new StubStrategyCatalog(new TwoParameterStrategy()));
        services.AddQuantBacktestEngine();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IParameterSweepService>().Should().BeOfType<ParameterSweepService>();
    }

    private sealed class DecimalStrategy : IStrategy
    {
        public DecimalStrategy(params ParameterDefinition[] parameters)
        {
            Parameters = parameters;
        }

        public string Id => "decimal";
        public string Name => "Decimal";
        public string Description => "Test.";
        public StrategyCategory Category => StrategyCategory.Baseline;
        public StrategyDocumentation Documentation => new("a", "b", "c", "d");
        public IReadOnlyList<Timeframe> SupportedTimeframes => new List<Timeframe> { Timeframe.Daily };
        public IReadOnlyList<ParameterDefinition> Parameters { get; }

        public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
        {
            return bars.Select(_ => Signal.Hold).ToList();
        }

        public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
        {
            return new List<IndicatorSeries>();
        }
    }
}
