namespace QuantBacktest.Strategies.Tests;

// Indicator tests with values computed by hand (PART-2 section 7)
public class IndicatorsTests
{
    private const double Tolerance = 1e-9;
    private const double NaN = double.NaN;

    [Fact]
    public void Sma_OneToFive_Period3() // I1
    {
        double[] result = Indicators.Sma(new double[] { 1, 2, 3, 4, 5 }, 3);

        TestArrays.ShouldBeClose(result, new double[] { NaN, NaN, 2, 3, 4 }, Tolerance);
    }

    [Fact]
    public void Ema_OneToFive_Period3_StartsWithTheSma() // I2
    {
        // alpha = 0.5: 2, then 0.5 x 4 + 0.5 x 2 = 3, then 0.5 x 5 + 0.5 x 3 = 4
        double[] result = Indicators.Ema(new double[] { 1, 2, 3, 4, 5 }, 3);

        TestArrays.ShouldBeClose(result, new double[] { NaN, NaN, 2, 3, 4 }, Tolerance);
    }

    [Fact]
    public void StdDev_IsThePopulationStandardDeviation() // I3
    {
        // mean = 5, sum of squared differences = 32, 32 / 8 = 4, sqrt(4) = 2 (the sample version would give 2.14)
        double[] result = Indicators.StdDev(new double[] { 2, 4, 4, 4, 5, 5, 7, 9 }, 8);

        result[7].Should().BeApproximately(2, Tolerance);
        double.IsNaN(result[6]).Should().BeTrue();
    }

    [Fact]
    public void Bollinger_ConstantSeries_ThreeBandsAreTheSame() // I4
    {
        double[] values = new double[30];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = 50;
        }

        BollingerBands bands = Indicators.Bollinger(values, 20, 2);

        double.IsNaN(bands.Middle[18]).Should().BeTrue();
        for (int i = 19; i < values.Length; i++)
        {
            bands.Upper[i].Should().BeApproximately(50, Tolerance);
            bands.Middle[i].Should().BeApproximately(50, Tolerance);
            bands.Lower[i].Should().BeApproximately(50, Tolerance);
        }
    }

    [Fact]
    public void Rsi_StrictlyRisingSeries_Is100() // I5
    {
        double[] result = Indicators.Rsi(new double[] { 1, 2, 3, 4, 5, 6, 7 }, 3);

        TestArrays.ShouldBeClose(result, new double[] { NaN, NaN, NaN, 100, 100, 100, 100 }, Tolerance);
    }

    [Fact]
    public void Rsi_StrictlyFallingSeries_Is0() // I5
    {
        double[] result = Indicators.Rsi(new double[] { 7, 6, 5, 4, 3, 2, 1 }, 3);

        TestArrays.ShouldBeClose(result, new double[] { NaN, NaN, NaN, 0, 0, 0, 0 }, Tolerance);
    }

    [Fact]
    public void Rsi_ConstantSeries_Is50() // I5
    {
        double[] result = Indicators.Rsi(new double[] { 4, 4, 4, 4, 4, 4 }, 3);

        TestArrays.ShouldBeClose(result, new double[] { NaN, NaN, NaN, 50, 50, 50 }, Tolerance);
    }

    [Fact]
    public void Roc_Period1_IsAFraction() // I6
    {
        double[] result = Indicators.Roc(new double[] { 100, 110, 121 }, 1);

        TestArrays.ShouldBeClose(result, new double[] { NaN, 0.1, 0.1 }, Tolerance);
    }

    [Fact]
    public void Macd_LineStartsAtSlowMinusOne_SignalAfterItsOwnWarmUp() // I7
    {
        double[] values = new double[] { 10, 11, 12, 11, 13, 14, 13, 15, 16, 15 };

        MacdResult macd = Indicators.Macd(values, 3, 5, 2);
        double[] fastEma = Indicators.Ema(values, 3);
        double[] slowEma = Indicators.Ema(values, 5);

        // line defined from index 5 - 1 = 4, signal from index 4 + 2 - 1 = 5
        double.IsNaN(macd.Line[3]).Should().BeTrue();
        macd.Line[4].Should().BeApproximately(fastEma[4] - slowEma[4], Tolerance);
        double.IsNaN(macd.Signal[4]).Should().BeTrue();
        macd.Signal[5].Should().BeApproximately((macd.Line[4] + macd.Line[5]) / 2, Tolerance);
    }

    [Fact]
    public void HighestHigh_DoesNotIncludeTheCurrentBar() // I8
    {
        double[] result = Indicators.HighestHigh(new double[] { 1, 2, 3, 10, 4 }, 2);

        // at index 3 the answer is 3, not 10 (10 is the current bar)
        TestArrays.ShouldBeClose(result, new double[] { NaN, NaN, 2, 3, 10 }, Tolerance);
    }

    [Fact]
    public void LowestLow_DoesNotIncludeTheCurrentBar() // I8
    {
        double[] result = Indicators.LowestLow(new double[] { 5, 4, 3, 1, 6 }, 2);

        TestArrays.ShouldBeClose(result, new double[] { NaN, NaN, 4, 3, 1 }, Tolerance);
    }

    [Fact]
    public void CrossesAbove_EqualOnThePreviousBar_IsACross() // I9
    {
        double[] a = new double[] { 2, 3 };
        double[] b = new double[] { 2, 2 };

        Indicators.CrossesAbove(a, b, 1).Should().BeTrue();
    }

    [Fact]
    public void CrossesAbove_AlreadyAbove_IsNotACross() // I9
    {
        double[] a = new double[] { 3, 4 };
        double[] b = new double[] { 2, 2 };

        Indicators.CrossesAbove(a, b, 1).Should().BeFalse();
    }

    [Fact]
    public void CrossesBelow_EqualOnThePreviousBar_IsACross() // I9
    {
        double[] a = new double[] { 2, 1 };
        double[] b = new double[] { 2, 2 };

        Indicators.CrossesBelow(a, b, 1).Should().BeTrue();
    }

    [Fact]
    public void Crosses_UndefinedValueOrFirstBar_IsNotACross() // I9
    {
        double[] a = new double[] { NaN, 3 };
        double[] b = new double[] { 2, 2 };

        Indicators.CrossesAbove(a, b, 1).Should().BeFalse();
        Indicators.CrossesBelow(b, a, 1).Should().BeFalse();
        Indicators.CrossesAbove(new double[] { 1 }, new double[] { 0 }, 0).Should().BeFalse();
    }

    [Fact]
    public void ToNullable_NaNBecomesNull() // P2-I2
    {
        double?[] result = Indicators.ToNullable(new double[] { NaN, 1.5 });

        result[0].Should().BeNull();
        result[1].Should().Be(1.5);
    }

    [Fact]
    public void ShortSeries_GivesOnlyNaN_WithoutException() // P2-I1, S2
    {
        double[] oneValue = new double[] { 100 };

        double.IsNaN(Indicators.Sma(oneValue, 20)[0]).Should().BeTrue();
        double.IsNaN(Indicators.Ema(oneValue, 20)[0]).Should().BeTrue();
        double.IsNaN(Indicators.Rsi(oneValue, 14)[0]).Should().BeTrue();
        double.IsNaN(Indicators.Roc(oneValue, 14)[0]).Should().BeTrue();
        double.IsNaN(Indicators.Macd(oneValue, 12, 26, 9).Signal[0]).Should().BeTrue();
        double.IsNaN(Indicators.HighestHigh(oneValue, 20)[0]).Should().BeTrue();
        Indicators.Bollinger(new double[0], 20, 2).Upper.Length.Should().Be(0);
    }

    [Fact]
    public void PeriodBelowOne_ThrowsArgumentOutOfRangeException() // P2-I1
    {
        bool thrown = false;
        try
        {
            Indicators.Sma(new double[] { 1, 2 }, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            thrown = true;
        }

        thrown.Should().BeTrue();
    }
}
