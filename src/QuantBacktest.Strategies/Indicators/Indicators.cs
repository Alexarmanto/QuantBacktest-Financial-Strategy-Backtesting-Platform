namespace QuantBacktest.Strategies;

// All the technical indicators used by the strategies (contract rules I1 to I9).
// Each method takes an array of values and returns an array of the same length.
// double.NaN means "not defined yet" (warm-up).
internal static class Indicators
{
    // I1: simple moving average of the last "period" values
    public static double[] Sma(double[] values, int period)
    {
        CheckPeriod(period);
        double[] result = NewNaNArray(values.Length);

        for (int i = period - 1; i < values.Length; i++)
        {
            double sum = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                sum = sum + values[j];
            }
            result[i] = sum / period;
        }

        return result;
    }

    // I2: exponential moving average, starts with the SMA of the first "period" values
    public static double[] Ema(double[] values, int period)
    {
        CheckPeriod(period);
        double[] result = NewNaNArray(values.Length);
        if (values.Length < period)
        {
            return result;
        }

        double alpha = 2.0 / (period + 1);

        double sum = 0;
        for (int i = 0; i < period; i++)
        {
            sum = sum + values[i];
        }
        result[period - 1] = sum / period;

        for (int i = period; i < values.Length; i++)
        {
            result[i] = alpha * values[i] + (1 - alpha) * result[i - 1];
        }

        return result;
    }

    // I3: population standard deviation (divided by n) of the last "period" values
    public static double[] StdDev(double[] values, int period)
    {
        CheckPeriod(period);
        double[] mean = Sma(values, period);
        double[] result = NewNaNArray(values.Length);

        for (int i = period - 1; i < values.Length; i++)
        {
            double sum = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                double difference = values[j] - mean[i];
                sum = sum + difference * difference;
            }
            result[i] = Math.Sqrt(sum / period);
        }

        return result;
    }

    // I4: middle = SMA, upper and lower = SMA +/- stdDevs x standard deviation
    public static BollingerBands Bollinger(double[] values, int period, double stdDevs)
    {
        double[] middle = Sma(values, period);
        double[] deviation = StdDev(values, period);
        double[] upper = new double[values.Length];
        double[] lower = new double[values.Length];

        // NaN + something stays NaN, so the warm-up is kept
        for (int i = 0; i < values.Length; i++)
        {
            upper[i] = middle[i] + stdDevs * deviation[i];
            lower[i] = middle[i] - stdDevs * deviation[i];
        }

        return new BollingerBands(upper, middle, lower);
    }

    // I5: RSI with Wilder's smoothing
    public static double[] Rsi(double[] values, int period)
    {
        CheckPeriod(period);
        double[] result = NewNaNArray(values.Length);
        if (values.Length <= period)
        {
            return result;
        }

        // first averages = simple average of the first "period" changes
        double averageGain = 0;
        double averageLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            double change = values[i] - values[i - 1];
            if (change > 0)
            {
                averageGain = averageGain + change;
            }
            else
            {
                averageLoss = averageLoss - change;
            }
        }
        averageGain = averageGain / period;
        averageLoss = averageLoss / period;
        result[period] = RsiFromAverages(averageGain, averageLoss);

        // then: (previous average x (period - 1) + current change) / period
        for (int i = period + 1; i < values.Length; i++)
        {
            double change = values[i] - values[i - 1];
            double gain = 0;
            double loss = 0;
            if (change > 0)
            {
                gain = change;
            }
            else
            {
                loss = -change;
            }

            averageGain = (averageGain * (period - 1) + gain) / period;
            averageLoss = (averageLoss * (period - 1) + loss) / period;
            result[i] = RsiFromAverages(averageGain, averageLoss);
        }

        return result;
    }

    // I6: rate of change = value / value "period" bars ago - 1 (a fraction: 0.1 = 10 %)
    public static double[] Roc(double[] values, int period)
    {
        CheckPeriod(period);
        double[] result = NewNaNArray(values.Length);

        for (int i = period; i < values.Length; i++)
        {
            result[i] = values[i] / values[i - period] - 1;
        }

        return result;
    }

    // I7: line = EMA(fast) - EMA(slow), signal = EMA of the defined values of the line
    public static MacdResult Macd(double[] values, int fast, int slow, int signal)
    {
        double[] fastEma = Ema(values, fast);
        double[] slowEma = Ema(values, slow);
        double[] line = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            line[i] = fastEma[i] - slowEma[i];
        }

        // find the first bar where the line is defined
        int first = 0;
        while (first < line.Length && double.IsNaN(line[first]))
        {
            first++;
        }

        // compute the signal on the defined part only, then put it back in place
        double[] signalLine = NewNaNArray(values.Length);
        if (first < line.Length)
        {
            double[] definedPart = new double[line.Length - first];
            for (int i = 0; i < definedPart.Length; i++)
            {
                definedPart[i] = line[first + i];
            }

            double[] definedSignal = Ema(definedPart, signal);
            for (int i = 0; i < definedSignal.Length; i++)
            {
                signalLine[first + i] = definedSignal[i];
            }
        }

        return new MacdResult(line, signalLine);
    }

    // I8: highest High of the "period" previous bars (the current bar is not included)
    public static double[] HighestHigh(double[] highs, int period)
    {
        CheckPeriod(period);
        double[] result = NewNaNArray(highs.Length);

        for (int i = period; i < highs.Length; i++)
        {
            double max = highs[i - period];
            for (int j = i - period + 1; j < i; j++)
            {
                if (highs[j] > max)
                {
                    max = highs[j];
                }
            }
            result[i] = max;
        }

        return result;
    }

    // I8: lowest Low of the "period" previous bars (the current bar is not included)
    public static double[] LowestLow(double[] lows, int period)
    {
        CheckPeriod(period);
        double[] result = NewNaNArray(lows.Length);

        for (int i = period; i < lows.Length; i++)
        {
            double min = lows[i - period];
            for (int j = i - period + 1; j < i; j++)
            {
                if (lows[j] < min)
                {
                    min = lows[j];
                }
            }
            result[i] = min;
        }

        return result;
    }

    // I9: a goes from below (or equal) to above b at bar i
    public static bool CrossesAbove(double[] a, double[] b, int i)
    {
        if (!AllDefined(a, b, i))
        {
            return false;
        }
        return a[i - 1] <= b[i - 1] && a[i] > b[i];
    }

    // I9: a goes from above (or equal) to below b at bar i
    public static bool CrossesBelow(double[] a, double[] b, int i)
    {
        if (!AllDefined(a, b, i))
        {
            return false;
        }
        return a[i - 1] >= b[i - 1] && a[i] < b[i];
    }

    // P2-I2: for IndicatorSeries, NaN becomes null
    public static double?[] ToNullable(double[] values)
    {
        double?[] result = new double?[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            if (!double.IsNaN(values[i]))
            {
                result[i] = values[i];
            }
        }

        return result;
    }

    private static double RsiFromAverages(double averageGain, double averageLoss)
    {
        if (averageGain == 0 && averageLoss == 0)
        {
            return 50;
        }
        if (averageLoss == 0)
        {
            return 100;
        }
        return 100 - 100 / (1 + averageGain / averageLoss);
    }

    // the 4 values a[i-1], b[i-1], a[i], b[i] must exist and be defined
    private static bool AllDefined(double[] a, double[] b, int i)
    {
        if (i < 1 || i >= a.Length || i >= b.Length)
        {
            return false;
        }
        return !double.IsNaN(a[i - 1]) && !double.IsNaN(b[i - 1])
            && !double.IsNaN(a[i]) && !double.IsNaN(b[i]);
    }

    private static double[] NewNaNArray(int length)
    {
        double[] result = new double[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = double.NaN;
        }
        return result;
    }

    private static void CheckPeriod(int period)
    {
        if (period < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "The period must be at least 1.");
        }
    }
}
