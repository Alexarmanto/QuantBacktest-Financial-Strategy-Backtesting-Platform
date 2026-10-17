namespace QuantBacktest.Strategies.Tests;

// Our indicators compared with values computed outside of our code, with pandas
// (method: Reference/indicators_reference.py). Tolerance 1e-9 (PART-2 section 7).
public class IndicatorsReferenceTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Bollinger_20_2_MatchesPandas() // I3, I4
    {
        BollingerBands bands = Indicators.Bollinger(IndicatorReferenceData.Close, 20, 2);

        TestArrays.ShouldBeClose(bands.Upper, IndicatorReferenceData.BollingerUpper, Tolerance);
        TestArrays.ShouldBeClose(bands.Middle, IndicatorReferenceData.BollingerMiddle, Tolerance);
        TestArrays.ShouldBeClose(bands.Lower, IndicatorReferenceData.BollingerLower, Tolerance);
    }

    [Fact]
    public void Macd_12_26_9_MatchesPandas() // I2, I7
    {
        MacdResult macd = Indicators.Macd(IndicatorReferenceData.Close, 12, 26, 9);

        TestArrays.ShouldBeClose(macd.Line, IndicatorReferenceData.MacdLine, Tolerance);
        TestArrays.ShouldBeClose(macd.Signal, IndicatorReferenceData.MacdSignal, Tolerance);
    }

    [Fact]
    public void Rsi_14_MatchesPandas() // I5
    {
        double[] rsi = Indicators.Rsi(IndicatorReferenceData.Close, 14);

        TestArrays.ShouldBeClose(rsi, IndicatorReferenceData.Rsi, Tolerance);
    }
}
