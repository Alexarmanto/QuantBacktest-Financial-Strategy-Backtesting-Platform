using QuantBacktest.Core.Errors;
using QuantBacktest.Core.Models;
using QuantBacktest.Engine.Tests.TestSupport;

namespace QuantBacktest.Engine.Tests;

/// <summary>What the engine refuses: bad period (X1), bad execution settings, a strategy that breaks its contract.</summary>
public class BacktestEngineValidationTests
{
    [Fact]
    public void FromAfterTo_IsRejected() // X1
    {
        Action run = () => ReferenceSeries.Run(
            "BHSHHH", from: ReferenceSeries.Dates[4], to: ReferenceSeries.Dates[2]);

        run.Should().Throw<BacktestValidationException>().WithMessage("*start date*");
    }

    [Fact]
    public void PeriodWithOneBar_IsRejected() // X1
    {
        Action run = () => ReferenceSeries.Run(
            "BHSHHH", from: ReferenceSeries.Dates[3], to: ReferenceSeries.Dates[3]);

        run.Should().Throw<BacktestValidationException>().WithMessage("*at least 2 bars*");
    }

    [Fact]
    public void PeriodWithoutAnyBar_IsRejected() // X1
    {
        Action run = () => ReferenceSeries.Run("BHSHHH", from: new DateOnly(2030, 1, 1));

        run.Should().Throw<BacktestValidationException>().WithMessage("*at least 2 bars*");
    }

    [Fact]
    public void BoundsAreInclusive() // X1
    {
        BacktestResult result = ReferenceSeries.Run(
            "HHHHHH", from: ReferenceSeries.Dates[1], to: ReferenceSeries.Dates[4]);

        result.Bars.Should().HaveCount(4);
        result.Bars[0].Date.Should().Be(ReferenceSeries.Dates[1]);
        result.Bars[3].Date.Should().Be(ReferenceSeries.Dates[4]);
    }

    [Fact]
    public void StrategyWithTheWrongNumberOfSignals_IsABug()
    {
        // a strategy that always returns 3 signals, whatever the number of bars
        IStrategyStub strategy = new();
        BacktestRequest request = ReferenceSeries.Request(ReferenceSeries.Settings());

        Action run = () => new BacktestEngine().Run(request, ReferenceSeries.Create(), strategy);

        run.Should().Throw<InvalidOperationException>().WithMessage("*3 signals for 6 bars*");
    }

    [Fact]
    public void StrategyException_IsNotWrapped()
    {
        ScriptedStrategy strategy = ScriptedStrategy.FromLetters("HHHHHH");
        strategy.ExceptionToThrow = new BacktestValidationException("fast must be lower than slow.");
        BacktestRequest request = ReferenceSeries.Request(ReferenceSeries.Settings());

        Action run = () => new BacktestEngine().Run(request, ReferenceSeries.Create(), strategy);

        run.Should().Throw<BacktestValidationException>().WithMessage("fast must be lower than slow.");
    }

    [Fact]
    public void IndicatorWithTheWrongLength_IsABug()
    {
        ScriptedStrategy strategy = ScriptedStrategy.FromLetters("HHHHHH");
        strategy.Indicators = new List<IndicatorSeries>
        {
            new("short", "Short", IndicatorPane.Price, new double?[] { 1, 2 }, new List<double>()),
        };
        BacktestRequest request = ReferenceSeries.Request(ReferenceSeries.Settings());

        Action run = () => new BacktestEngine().Run(request, ReferenceSeries.Create(), strategy);

        run.Should().Throw<InvalidOperationException>().WithMessage("*'short'*");
    }

    [Theory]
    [InlineData(0, "Initial capital")]
    [InlineData(-5, "Initial capital")]
    public void InitialCapital_MustBePositive(int capital, string field)
    {
        ExecutionSettings settings = ReferenceSeries.Settings() with { InitialCapital = capital };

        Action run = () => ReferenceSeries.Run("HHHHHH", settings);

        run.Should().Throw<BacktestValidationException>().WithMessage($"*{field}*");
    }

    [Fact]
    public void FeeAboveTheMaximum_IsRejected()
    {
        Action run = () => ReferenceSeries.Run("HHHHHH", ReferenceSeries.Settings(fee: 0.06m));

        run.Should().Throw<BacktestValidationException>().WithMessage("Fee rate must be in [0, 0.05].");
    }

    [Fact]
    public void NegativeSlippage_IsRejected()
    {
        Action run = () => ReferenceSeries.Run("HHHHHH", ReferenceSeries.Settings(slippage: -0.001m));

        run.Should().Throw<BacktestValidationException>().WithMessage("Slippage rate*");
    }

    [Theory]
    [InlineData(0.0)]  // zero is not allowed: the range is ]0, 0.5]
    [InlineData(0.6)]  // above MaxStopLossFraction
    public void StopLossOutOfRange_IsRejected(double value)
    {
        Action run = () => ReferenceSeries.Run("HHHHHH", ReferenceSeries.Settings(stopLoss: (decimal)value));

        run.Should().Throw<BacktestValidationException>().WithMessage("Stop-loss must be in ]0, 0.5].");
    }

    [Fact]
    public void TakeProfitAboveTheMaximum_IsRejected()
    {
        Action run = () => ReferenceSeries.Run("HHHHHH", ReferenceSeries.Settings(takeProfit: 6m));

        run.Should().Throw<BacktestValidationException>().WithMessage("Take-profit must be in ]0, 5].");
    }

    [Fact]
    public void TrailingStopAboveTheMaximum_IsRejected()
    {
        Action run = () => ReferenceSeries.Run("HHHHHH", ReferenceSeries.Settings(trailingStop: 0.51m));

        run.Should().Throw<BacktestValidationException>().WithMessage("Trailing stop must be in ]0, 0.5].");
    }
    [Fact]
    public void RiskFreeRateAboveTheMaximum_IsRejected()
    {
        ExecutionSettings settings = ReferenceSeries.Settings() with { RiskFreeRate = 0.5 };

        Action run = () => ReferenceSeries.Run("HHHHHH", settings);

        run.Should().Throw<BacktestValidationException>().WithMessage("Risk-free rate*");
    }

    /// <summary>A strategy that ignores the bars it receives and always returns 3 signals.</summary>
    private sealed class IStrategyStub : QuantBacktest.Core.Abstractions.IStrategy
    {
        public string Id => "stub";
        public string Name => "Stub";
        public string Description => "Stub.";
        public StrategyCategory Category => StrategyCategory.Baseline;
        public StrategyDocumentation Documentation => new("a", "b", "c", "d");
        public IReadOnlyList<Timeframe> SupportedTimeframes => new List<Timeframe> { Timeframe.Daily };
        public IReadOnlyList<ParameterDefinition> Parameters => new List<ParameterDefinition>();

        public IReadOnlyList<Signal> GenerateSignals(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
        {
            return new List<Signal> { Signal.Hold, Signal.Hold, Signal.Hold };
        }

        public IReadOnlyList<IndicatorSeries> ComputeIndicators(IReadOnlyList<PriceBar> bars, IReadOnlyDictionary<string, double> parameters)
        {
            return new List<IndicatorSeries>();
        }
    }
}
