namespace QuantBacktest.Strategies;

// The MACD line and its signal line (I7), one value per bar
internal class MacdResult
{
    public double[] Line { get; private set; }
    public double[] Signal { get; private set; }

    // Constructor
    public MacdResult(double[] line, double[] signal)
    {
        Line = line;
        Signal = signal;
    }
}
