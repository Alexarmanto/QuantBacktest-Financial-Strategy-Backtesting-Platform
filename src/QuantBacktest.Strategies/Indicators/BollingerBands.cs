namespace QuantBacktest.Strategies;

// The three Bollinger bands (I4), one value per bar
internal class BollingerBands
{
    public double[] Upper { get; private set; }
    public double[] Middle { get; private set; }
    public double[] Lower { get; private set; }

    // Constructor
    public BollingerBands(double[] upper, double[] middle, double[] lower)
    {
        Upper = upper;
        Middle = middle;
        Lower = lower;
    }
}
