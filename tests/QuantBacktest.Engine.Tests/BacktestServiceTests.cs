using Microsoft.Extensions.DependencyInjection;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Engine.Tests.TestSupport;

namespace QuantBacktest.Engine.Tests;

/// <summary>P3-E4: the validation of a request and the way the service chains the catalog, the data and the engine.</summary>
public class BacktestServiceTests
{
    // a strategy with two parameters: "fast" (Integer, default 5, [2, 20]) and "level" (Decimal, default 0.5, [0, 1])
    private static ScriptedStrategy CreateStrategy(IReadOnlyList<Timeframe>? timeframes = null)
    {
        List<ParameterDefinition> parameters = new()
        {
            new("fast", "Fast", ParameterType.Integer, 5, 2, 20, 1),
            new("level", "Level", ParameterType.Decimal, 0.5, 0, 1, 0.1),
        };
        ScriptedStrategy strategy = new(new List<Signal>(), parameters, timeframes);
        return strategy;
    }

    private static (BacktestService service, StubMarketDataService data, StubStrategyCatalog catalog) CreateService(
        IStrategy? strategy = null)
    {
        StubMarketDataService data = new(ReferenceSeries.Create());
        StubStrategyCatalog catalog = new(strategy ?? CreateStrategy());
        return (new BacktestService(data, catalog, new BacktestEngine()), data, catalog);
    }

    private static BacktestRequest Request(
        Dictionary<string, double>? parameters = null,
        string symbol = "TEST",
        string strategyId = "scripted",
        Timeframe timeframe = Timeframe.Daily,
        ExecutionSettings? execution = null)
    {
        return new BacktestRequest(
            symbol, timeframe, strategyId, parameters ?? new Dictionary<string, double>(), null, null,
            execution ?? ReferenceSeries.Settings());
    }

    [Fact]
    public async Task ValidRequest_RunsTheEngine()
    {
        var (service, data, _) = CreateService();

        BacktestResult result = await service.RunAsync(Request());

        result.Bars.Should().HaveCount(6);
        result.StrategyName.Should().Be("Scripted strategy");
        data.CallCount.Should().Be(1);
        data.LastForceRefresh.Should().BeFalse();
    }

    [Fact]
    public async Task MissingParameters_AreCompletedWithTheirDefaults_InTheResultRequest() // P3-E5
    {
        var (service, _, _) = CreateService();

        BacktestResult result = await service.RunAsync(Request(new Dictionary<string, double> { ["fast"] = 8 }));

        result.Request.Parameters["fast"].Should().Be(8);
        result.Request.Parameters["level"].Should().Be(0.5);
        result.Request.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public async Task SymbolIsNormalisedBeforeTheDataCall()
    {
        var (service, data, _) = CreateService();

        await service.RunAsync(Request(symbol: "  test "));

        data.LastSymbol.Should().Be("TEST");
    }

    [Fact]
    public async Task UnknownStrategy_IsRejected()
    {
        var (service, data, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Request(strategyId: "nope"));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("Unknown strategy 'nope'.");
        data.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TimeframeNotSupported_NamesTheStrategyAndTheTimeframe()
    {
        var (service, data, _) = CreateService(CreateStrategy(new List<Timeframe> { Timeframe.Daily }));

        Func<Task> run = () => service.RunAsync(Request(timeframe: Timeframe.Weekly));

        (await run.Should().ThrowAsync<BacktestValidationException>())
            .WithMessage("Strategy 'Scripted strategy' does not support the Weekly timeframe.");
        data.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UnknownParameterKey_IsRejected()
    {
        var (service, _, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Request(new Dictionary<string, double> { ["slow"] = 3 }));

        (await run.Should().ThrowAsync<BacktestValidationException>())
            .WithMessage("Unknown parameter 'slow' for strategy 'Scripted strategy'.");
    }

    [Theory]
    [InlineData(1.0)]    // under Min
    [InlineData(21.0)]   // over Max
    [InlineData(double.NaN)]
    public async Task ParameterOutOfRange_NamesTheParameterAndItsRange(double value)
    {
        var (service, _, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Request(new Dictionary<string, double> { ["fast"] = value }));

        (await run.Should().ThrowAsync<BacktestValidationException>())
            .WithMessage("Parameter 'fast' must be between 2 and 20.");
    }

    [Fact]
    public async Task IntegerParameter_RejectsAFraction()
    {
        var (service, _, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Request(new Dictionary<string, double> { ["fast"] = 5.5 }));

        (await run.Should().ThrowAsync<BacktestValidationException>())
            .WithMessage("Parameter 'fast' must be a whole number between 2 and 20.");
    }

    [Fact]
    public async Task DecimalParameter_AcceptsAFraction()
    {
        var (service, _, _) = CreateService();

        BacktestResult result = await service.RunAsync(Request(new Dictionary<string, double> { ["level"] = 0.25 }));

        result.Request.Parameters["level"].Should().Be(0.25);
    }

    [Fact]
    public async Task ParameterBounds_AreAccepted()
    {
        var (service, _, _) = CreateService();

        BacktestResult result = await service.RunAsync(
            Request(new Dictionary<string, double> { ["fast"] = 2, ["level"] = 1 }));

        result.Request.Parameters["fast"].Should().Be(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOO-LONG-SYMBOL")]
    [InlineData("BAD SYMBOL")]
    public async Task InvalidSymbol_IsRejected(string symbol)
    {
        var (service, data, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Request(symbol: symbol));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("Symbol must be*");
        data.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task InvalidExecutionSettings_AreRejected_BeforeTheDataCall()
    {
        var (service, data, _) = CreateService();

        Func<Task> run = () => service.RunAsync(Request(execution: ReferenceSeries.Settings(fee: 0.9m)));

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("Fee rate must be in [0, 0.05].");
        data.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MarketDataException_IsNotWrapped()
    {
        var (service, data, _) = CreateService();
        data.ExceptionToThrow = new MarketDataException(MarketDataErrorKind.RateLimitExceeded, "Daily limit reached.");

        Func<Task> run = () => service.RunAsync(Request());

        MarketDataException error = (await run.Should().ThrowAsync<MarketDataException>()).Subject.Single();
        error.Kind.Should().Be(MarketDataErrorKind.RateLimitExceeded);
        error.Message.Should().Be("Daily limit reached.");
    }

    [Fact]
    public async Task TwoIdenticalCalls_GiveTheSameResult() // P3-E6
    {
        var (service, _, _) = CreateService();

        BacktestResult first = await service.RunAsync(Request());
        BacktestResult second = await service.RunAsync(Request());

        second.Metrics.Should().Be(first.Metrics);
        second.Equity.Should().Equal(first.Equity);
        second.Request.Parameters.Should().Equal(first.Request.Parameters);
    }

    [Fact]
    public async Task StrategyValidationException_PassesThrough()
    {
        ScriptedStrategy strategy = CreateStrategy();
        strategy.ExceptionToThrow = new BacktestValidationException("fast must be lower than slow.");
        var (service, _, _) = CreateService(strategy);

        Func<Task> run = () => service.RunAsync(Request());

        (await run.Should().ThrowAsync<BacktestValidationException>()).WithMessage("fast must be lower than slow.");
    }

    [Fact]
    public void AddQuantBacktestEngine_RegistersTheServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<IMarketDataService>(new StubMarketDataService(ReferenceSeries.Create()));
        services.AddSingleton<IStrategyCatalog>(new StubStrategyCatalog(CreateStrategy()));
        services.AddQuantBacktestEngine();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBacktestEngine>().Should().BeOfType<BacktestEngine>();
        provider.GetRequiredService<IBacktestService>().Should().BeOfType<BacktestService>();
        provider.GetRequiredService<IBacktestService>().Should().BeSameAs(provider.GetRequiredService<IBacktestService>());
    }
}
