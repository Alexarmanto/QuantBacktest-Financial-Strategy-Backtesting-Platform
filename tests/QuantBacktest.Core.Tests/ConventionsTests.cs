using System.Text.RegularExpressions;
using QuantBacktest.Core.Models;

namespace QuantBacktest.Core.Tests;

public class ConventionsTests
{
    [Theory]
    [InlineData("Trend dip", "custom-trend-dip")]
    [InlineData("  My RSI dip #2  ", "custom-my-rsi-dip-2")]
    [InlineData("SMA 50/200 -- golden", "custom-sma-50-200-golden")]
    [InlineData("Été 2026", "custom-t-2026")]
    public void CreateCustomStrategyId_BuildsTheExpectedSlug(string name, string expected)
    {
        var id = Conventions.CreateCustomStrategyId(name);

        id.Should().Be(expected);
        Regex.IsMatch(id, Conventions.CustomStrategyIdPattern).Should().BeTrue();
    }

    [Fact]
    public void CreateCustomStrategyId_TruncatesLongNames()
    {
        var id = Conventions.CreateCustomStrategyId(new string('a', 100));

        id.Should().Be(Conventions.CustomStrategyIdPrefix + new string('a', Conventions.CustomStrategySlugMaxLength));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#!?")]
    public void CreateCustomStrategyId_RejectsNamesWithoutLetterOrDigit(string name)
    {
        var act = () => Conventions.CreateCustomStrategyId(name);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(Timeframe.Daily, 252)]
    [InlineData(Timeframe.Weekly, 52)]
    public void PeriodsPerYear_MatchesTheContract(Timeframe timeframe, int expected)
    {
        Conventions.PeriodsPerYear(timeframe).Should().Be(expected);
    }

    [Fact]
    public void ExecutionSettingsDefault_MatchesTheContract()
    {
        var settings = ExecutionSettings.Default;

        settings.InitialCapital.Should().Be(10_000m);
        settings.FeeRate.Should().Be(0.001m);
        settings.SlippageRate.Should().Be(0m);
        settings.StopLossFraction.Should().BeNull();
        settings.TakeProfitFraction.Should().BeNull();
        settings.TrailingStopFraction.Should().BeNull();
        settings.RiskFreeRate.Should().Be(0.0);
    }

    [Fact]
    public void ApiQuotaStatus_RemainingNeverNegative()
    {
        var reset = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

        new ApiQuotaStatus(17, 25, reset).Remaining.Should().Be(8);
        new ApiQuotaStatus(30, 25, reset).Remaining.Should().Be(0);
    }
}
