using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using QuantBacktest.Core;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Web.Models;
using QuantBacktest.Web.Tests.TestDoubles;

namespace QuantBacktest.Web.Tests.Models;

public class BacktestQueryOptimizationTests
{
    private const string Settings = "symbol=AAPL&tf=daily&strategy=fake-threshold&capital=10000&fee=0.1&slip=0&rf=0";

    /// <summary>fake-threshold with the P4-X2 defaults: lookback 1..100 step 5, threshold 0..0.5 step 0.025, Sharpe.</summary>
    private const string Canonical = Settings + "&x=lookback&xmin=1&xmax=100&xstep=5&y=threshold&ymin=0&ymax=0.5&ystep=0.025&metric=sharpe";

    private const string RsiSettings = "symbol=AAPL&tf=daily&strategy=rsi-reversion&capital=10000&fee=0.1&slip=0&rf=0";

    internal static readonly StubStrategy Threshold = new()
    {
        Id = "fake-threshold",
        Name = "Threshold",
        Parameters =
        [
            new ParameterDefinition("lookback", "Lookback (bars)", ParameterType.Integer, 10, 1, 100, 1),
            new ParameterDefinition("threshold", "Threshold (fraction)", ParameterType.Decimal, 0.02, 0, 0.5, 0.005),
        ],
    };

    /// <summary>The definitions of rsi-reversion (Strategies/RsiReversionStrategy.cs:23-25): 3 parameters.</summary>
    internal static readonly StubStrategy Rsi = new()
    {
        Id = "rsi-reversion",
        Name = "RSI mean reversion",
        Parameters =
        [
            new ParameterDefinition("period", "RSI period (bars)", ParameterType.Integer, 14, 2, 50, 1),
            new ParameterDefinition("oversold", "Oversold level", ParameterType.Decimal, 30, 5, 50, 1),
            new ParameterDefinition("overbought", "Overbought level", ParameterType.Decimal, 70, 50, 95, 1),
        ],
    };

    private static readonly StubStrategy NoParameters = new() { Id = "custom-trend-dip", Name = "Trend dip" };

    private static readonly StubStrategy OneParameter = new()
    {
        Id = "one",
        Name = "One",
        Parameters = [new ParameterDefinition("period", "Period", ParameterType.Integer, 10, 2, 50, 1)],
    };

    private static IQueryCollection Query(string queryString) => new QueryCollection(QueryHelpers.ParseQuery(queryString));

    private static OptimizationQuery Parse(string queryString)
    {
        BacktestQuery.TryParseOptimization(Query(queryString), out var result, out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        return result!;
    }

    private static IReadOnlyList<QueryFieldError> ParseErrors(string queryString)
    {
        BacktestQuery.TryParseOptimization(Query(queryString), out var result, out var errors).Should().BeFalse();
        result.Should().BeNull();
        return errors;
    }

    private static OptimizationQuery Resolve(string queryString, IStrategy strategy)
    {
        var errors = Parse(queryString).Resolve(strategy, out var effective);
        errors.Should().BeEmpty();
        return effective!;
    }

    private static IReadOnlyList<QueryFieldError> ResolveErrors(string queryString, IStrategy strategy)
    {
        var errors = Parse(queryString).Resolve(strategy, out var effective);
        effective.Should().BeNull();
        return errors;
    }

    private static string Axes(string x = "x=lookback&xmin=1&xmax=100&xstep=5", string y = "y=threshold&ymin=0&ymax=0.5&ystep=0.025") =>
        $"&{x}&{y}&metric=sharpe";

    /// <summary>
    /// The enumeration of the service, written out by hand (Engine/ParameterSweepService.cs:162 and 170-172):
    /// double count = Math.Floor(((axis.Max - axis.Min) / axis.Step) + 1e-9) + 1;
    /// for (int k = 0; k &lt; (int)count; k++) values.Add(Math.Round(axis.Min + (k * axis.Step), 10));
    /// </summary>
    private static List<double> ServiceEnumeration(double min, double max, double step)
    {
        double count = Math.Floor(((max - min) / step) + 1e-9) + 1;
        var values = new List<double>();
        for (int k = 0; k < (int)count; k++)
        {
            values.Add(Math.Round(min + (k * step), 10));
        }

        return values;
    }

    // ---- URL ----

    [Fact] // P4-X2: URL → model → URL gives the same canonical string
    public void Optimization_RoundTrip_CanonicalUrlUnchanged()
    {
        var effective = Resolve(Canonical, Threshold);

        effective.ToQueryString().Should().Be(Canonical);
        effective.IsCanonical(new QueryString("?" + Canonical)).Should().BeTrue();
    }

    [Fact] // P4-X2 defaults: axes = first two parameters, ranges = definition bounds and Step × ceil((Max − Min) / (24 × Step)), Sharpe
    public void Optimization_AbsentAxesAndMetric_GetTheDefaults()
    {
        Resolve(Settings, Threshold).ToQueryString().Should().Be(Canonical);
    }

    [Fact] // P4-X2: the parameters that are not swept are settings of the backtest, explicit, between rf and x
    public void Canonical_OtherParametersBetweenSettingsAndAxes()
    {
        var effective = Resolve(RsiSettings + "&p.overbought=75", Rsi);

        effective.ToQueryString().Should().Be(
            RsiSettings + "&p.overbought=75&x=period&xmin=2&xmax=50&xstep=2&y=oversold&ymin=5&ymax=50&ystep=2&metric=sharpe");
    }

    [Fact] // section 3 of the contract (absent key = default), pinned in the permalink as on /Backtest
    public void Canonical_AbsentOtherParameter_GetsTheCatalogDefault()
    {
        Resolve(RsiSettings, Rsi).ToQueryString().Should().Contain("&p.overbought=70&x=period&");
    }

    [Fact] // O2: the swept parameters are replaced in every cell, so their p.* is dropped, never checked
    public void Canonical_DropsSweptParameters()
    {
        var effective = Resolve(Settings + "&p.lookback=999&p.threshold=0.02" + Axes(), Threshold);

        effective.ToQueryString().Should().Be(Canonical);
        effective.Settings.Parameters.Should().BeEmpty();
    }

    [Fact] // P4-X2: a valid query in another order or with encoded characters is canonical once decoded
    public void IsCanonical_ComparesDecodedPairs()
    {
        var effective = Resolve(Canonical, Threshold);

        effective.IsCanonical(new QueryString("?" + Canonical.Replace("ymax=0.5", "ymax=0%2E5"))).Should().BeTrue();
        effective.IsCanonical(new QueryString("?" + Canonical.Replace("&metric=sharpe", ""))).Should().BeFalse();
        effective.IsCanonical(new QueryString("?metric=sharpe&" + Canonical.Replace("&metric=sharpe", ""))).Should().BeFalse();
    }

    // ---- Reading ----

    [Fact] // P4-U16: the settings are read by the shared reader, so the errors are those of TryParse
    public void Optimization_Settings_ReadByTheSharedReader()
    {
        var url = "symbol=aa%20pl&capital=-1&fee=9&strategy=fake-threshold";
        BacktestQuery.TryParse(Query(url), out _, out var backtestErrors).Should().BeFalse();

        ParseErrors(url).Should().BeEquivalentTo(backtestErrors, options => options.WithStrictOrdering());
    }

    [Theory] // PART-4 section 7: invalid URL parameters → validation errors, no exception
    [InlineData("xmin=abc", "xmin", "X minimum must be a number, with '.' as the decimal separator.")]
    [InlineData("ystep=0,025", "ystep", "Y step must be a number, with '.' as the decimal separator.")]
    [InlineData("x=lookback&x=threshold", "x", "X parameter is given more than once.")]
    [InlineData("metric=drawdown", "metric", "Metric must be one of: sharpe, sortino, calmar, cumulative-return, annualized-return, profit-factor.")]
    public void Optimization_InvalidParameter_IsFieldError(string extra, string field, string message)
    {
        ParseErrors(Settings + "&" + extra).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(field, message));
    }

    [Theory] // P4-X2: metric is one of the SweepMetric values, any case; absent → Sharpe
    [InlineData("metric=sharpe", SweepMetric.SharpeRatio)]
    [InlineData("metric=SORTINO", SweepMetric.SortinoRatio)]
    [InlineData("metric=calmar", SweepMetric.CalmarRatio)]
    [InlineData("metric=cumulative-return", SweepMetric.CumulativeReturn)]
    [InlineData("metric=annualized-return", SweepMetric.AnnualizedReturn)]
    [InlineData("metric=profit-factor", SweepMetric.ProfitFactor)]
    [InlineData("metric=", SweepMetric.SharpeRatio)]
    [InlineData("tf=daily", SweepMetric.SharpeRatio)]
    public void Metric_Tokens_MapToSweepMetric(string extra, SweepMetric expected)
    {
        Parse(Settings.Replace("tf=daily", extra)).Metric.Should().Be(expected);
    }

    [Fact] // the token table is the only spelling, both ways
    public void Metric_FormatAndRead_AreInverse()
    {
        foreach (var metric in Enum.GetValues<SweepMetric>())
        {
            BacktestQuery.TryReadMetric(BacktestQuery.FormatMetric(metric), out var read).Should().BeTrue();
            read.Should().Be(metric);
        }

        BacktestQuery.TryReadMetric("0", out _).Should().BeFalse();
    }

    // ---- O1 checks, made by the web before any call ----

    [Fact] // O1: the two keys are distinct parameters
    public void Validate_SameParameterOnXAndY_IsError()
    {
        ResolveErrors(Settings + Axes(y: "y=lookback&ymin=1&ymax=100&ystep=5"), Threshold).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError("y", OptimizationQuery.SameParameterMessage));
    }

    [Theory] // O1: both keys are parameters of the strategy
    [InlineData("x=foo&xmin=1&xmax=100&xstep=5", "x")]
    [InlineData("x=lookback&xmin=1&xmax=100&xstep=5&y=bar", "y")]
    public void Validate_UnknownParameter_IsError(string axes, string field)
    {
        var url = Settings + "&" + axes;
        var key = field == "x" ? "foo" : "bar";
        ResolveErrors(url, Threshold).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(field, $"Unknown parameter '{key}' for this strategy."));
    }

    [Fact] // O1: Min <= Max
    public void Validate_MinGreaterThanMax_IsError()
    {
        ResolveErrors(Settings + Axes(x: "x=lookback&xmin=50&xmax=10&xstep=5"), Threshold).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError("xmin", "X minimum must not be greater than X maximum."));
    }

    [Theory] // O1: Step > 0
    [InlineData("0")]
    [InlineData("-5")]
    public void Validate_StepZeroOrNegative_IsError(string step)
    {
        ResolveErrors(Settings + Axes(x: $"x=lookback&xmin=1&xmax=100&xstep={step}"), Threshold).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError("xstep", "X step must be greater than 0."));
    }

    [Theory] // O1: bounds inside the [Min, Max] of the definition
    [InlineData("x=lookback&xmin=0&xmax=100&xstep=5", "xmin", "X minimum must be between 1 and 100 (Lookback (bars)).")]
    [InlineData("x=lookback&xmin=1&xmax=1000&xstep=5", "xmax", "X maximum must be between 1 and 100 (Lookback (bars)).")]
    public void Validate_BoundOutsideDefinition_IsError(string axis, string field, string message)
    {
        ResolveErrors(Settings + Axes(x: axis), Threshold).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(field, message));
    }

    [Theory] // O1: whole values for an Integer parameter (decision g-4: min, max and step all whole)
    [InlineData("x=lookback&xmin=1.5&xmax=100&xstep=5", "xmin", "minimum")]
    [InlineData("x=lookback&xmin=1&xmax=99.5&xstep=5", "xmax", "maximum")]
    [InlineData("x=lookback&xmin=1&xmax=100&xstep=2.5", "xstep", "step")]
    public void Validate_IntegerParameter_NonWholeValue_IsError(string axis, string field, string label)
    {
        ResolveErrors(Settings + Axes(x: axis), Threshold).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError(field, $"X {label} must be a whole number (Lookback (bars) takes whole numbers)."));
    }

    [Fact] // O1: a Decimal parameter accepts fractions
    public void Validate_DecimalParameter_AcceptsFractions()
    {
        Resolve(Settings + Axes(y: "y=threshold&ymin=0.005&ymax=0.1&ystep=0.005"), Threshold).Y.Step.Should().Be(0.005m);
    }

    [Fact] // O1: Min = Max is allowed: one value on that axis
    public void Validate_SingleValueRange_IsAccepted()
    {
        var request = Resolve(Settings + Axes(x: "x=lookback&xmin=10&xmax=10&xstep=5"), Threshold).ToSweepRequest();

        request.X.Should().Be(new SweepAxis("lookback", 10, 10, 5));
    }

    [Theory] // O1: the strategy must have at least 2 parameters
    [InlineData(0)]
    [InlineData(1)]
    public void Validate_FewerThanTwoParameters_IsError(int count)
    {
        IStrategy strategy = count == 0 ? NoParameters : OneParameter;

        ResolveErrors(Settings.Replace("fake-threshold", strategy.Id), strategy).Should().ContainSingle()
            .Which.Should().Be(new QueryFieldError("strategy", $"{strategy.Name} has fewer than 2 parameters: a heatmap needs one for X and one for Y."));
    }

    [Fact] // P4-U1 / section 3: the other parameters are checked as on /Backtest
    public void Validate_OtherParameterOutOfRange_IsError()
    {
        ResolveErrors(RsiSettings + "&p.overbought=99", Rsi).Should().ContainSingle()
            .Which.Field.Should().Be("p.overbought");
    }

    [Fact] // O1, Conventions.MaxSweepCells: 25 × 25 = 625 is accepted, 26 × 25 = 650 is refused before any call
    public void Validate_MaxSweepCells_625Accepted_650Refused()
    {
        Conventions.MaxSweepCells.Should().Be(625);
        var y = "y=threshold&ymin=0&ymax=0.12&ystep=0.005";
        Resolve(Settings + Axes("x=lookback&xmin=1&xmax=25&xstep=1", y), Threshold).Should().NotBeNull();

        var errors = ResolveErrors(Settings + Axes("x=lookback&xmin=1&xmax=26&xstep=1", y), Threshold);

        errors.Should().Equal(
            new QueryFieldError(
                OptimizationQuery.TooManyCellsField,
                "The heatmap would have 650 cells (26 × 25); the maximum is 625. Increase a step or narrow a range."),
            new QueryFieldError("xstep", OptimizationQuery.TooManyCellsHint),
            new QueryFieldError("ystep", OptimizationQuery.TooManyCellsHint));
    }

    [Fact] // O1: a tiny step is refused by counting, without building the list
    public void Validate_HugeGrid_IsRefusedByCounting()
    {
        var errors = ResolveErrors(Settings + Axes("x=lookback&xmin=1&xmax=100&xstep=1", "y=threshold&ymin=0&ymax=0.5&ystep=0.005"), Threshold);

        errors[0].Message.Should().StartWith("The heatmap would have 10100 cells (100 × 101)");
    }

    // ---- Same count as the service (correction A) ----

    [Theory] // O1: the web counts exactly the values the service enumerates
    [InlineData(0, 0.12, 0.005, 25)]
    [InlineData(0, 0.5, 0.025, 21)]
    [InlineData(1, 100, 5, 20)]
    [InlineData(2, 200, 9, 23)]
    [InlineData(0, 0.1, 0.001, 101)]
    [InlineData(0, 0.3, 0.1, 4)]
    public void CellCount_EqualsServiceEnumeration(double min, double max, double step, int expected)
    {
        var count = BacktestQuery.AxisValueCount((decimal)min, (decimal)max, (decimal)step);

        count.Should().Be(ServiceEnumeration(min, max, step).Count);
        count.Should().Be(expected);
    }

    [Theory] // P4-X2: default step = Step × ceil((Max − Min) / (24 × Step)), at most 25 values
    [InlineData(2, 200, 1, 9)]
    [InlineData(5, 400, 1, 17)]
    [InlineData(0, 0.5, 0.005, 0.025)]
    [InlineData(0, 1, 1, 1)]
    [InlineData(50, 300, 10, 20)]
    [InlineData(0, 0.1, 0.001, 0.005)]
    public void DefaultSweepStep_FollowsPartFourFormula(double min, double max, double step, double expected)
    {
        var definition = new ParameterDefinition("k", "K", ParameterType.Decimal, min, min, max, step);

        var defaultStep = BacktestQuery.DefaultSweepStep(definition);

        defaultStep.Should().Be((decimal)expected);
        BacktestQuery.AxisValueCount((decimal)min, (decimal)max, defaultStep).Should().BeLessThanOrEqualTo(25);
    }

    // ---- Contract request and cell links ----

    [Fact] // P4-X2, O2: one SweepRequest: percent → fraction, the other parameters only, the axes as doubles, the metric
    public void ToSweepRequest_ConvertsPercentAndAxes()
    {
        var request = Resolve(RsiSettings.Replace("fee=0.1", "fee=0.25") + "&p.overbought=75&p.period=7" + "&x=period&xmin=2&xmax=20&xstep=2&y=oversold&ymin=10&ymax=40&ystep=5&metric=calmar", Rsi)
            .ToSweepRequest();

        request.BaseRequest.Execution.FeeRate.Should().Be(0.0025m);
        request.BaseRequest.Parameters.Should().Equal(new Dictionary<string, double> { ["overbought"] = 75 });
        request.X.Should().Be(new SweepAxis("period", 2, 20, 2));
        request.Y.Should().Be(new SweepAxis("oversold", 10, 40, 5));
        request.Metric.Should().Be(SweepMetric.CalmarRatio);
    }

    [Fact] // P4-X2: the link of a cell is the canonical /Backtest/Result query with the two values of the cell
    public void ForCell_IsCanonicalBacktestResultQuery()
    {
        var cell = Resolve(Canonical, Threshold).ForCell(21, 0.1);

        cell.ToQueryString().Should().Be(Settings + "&p.lookback=21&p.threshold=0.1");
        cell.IsCanonical(new QueryString("?" + cell.ToQueryString())).Should().BeTrue();
    }

    [Fact] // correction B: binary noise of an axis value never reaches the URL
    public void ForCell_FloatingNoise_WritesTheShortestValue()
    {
        var cell = Resolve(Canonical, Threshold).ForCell(21, 0.07500000000000001);

        cell.ToQueryString().Should().EndWith("&p.threshold=0.075");
        BacktestQuery.FormatNumber(0.07500000000000001).Should().Be("0.075");
    }
}
