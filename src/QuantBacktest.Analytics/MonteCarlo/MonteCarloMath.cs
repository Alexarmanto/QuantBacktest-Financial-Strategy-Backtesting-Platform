using QuantBacktest.Core.Models;

namespace QuantBacktest.Analytics;

// The small calculations of the Monte Carlo simulation (CONTRACT section 9), each one testable alone
internal static class MonteCarloMath
{
    // MC3: start at V0 = 1, then V = V x (1 + r) for each trade; final return = last V - 1
    public static double FinalReturn(double[] returns)
    {
        double value = 1;
        for (int j = 0; j < returns.Length; j++)
        {
            value = value * (1 + returns[j]);
        }
        return value - 1;
    }

    // MC3: largest fall from a peak along the same path (the peak starts at V0 = 1)
    public static double MaxDrawdown(double[] returns)
    {
        double value = 1;
        double peak = 1;
        double maxDrawdown = 0;
        for (int j = 0; j < returns.Length; j++)
        {
            value = value * (1 + returns[j]);
            if (value > peak)
            {
                peak = value;
            }

            double drawdown = (peak - value) / peak;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }
        return maxDrawdown;
    }

    // MC4: percentile p (0.05 = 5 %) of values sorted from small to large, with a linear interpolation
    public static double Percentile(double[] sortedValues, double p)
    {
        double h = p * (sortedValues.Length - 1);
        int low = (int)Math.Floor(h);
        int high = (int)Math.Ceiling(h);
        return sortedValues[low] + (h - low) * (sortedValues[high] - sortedValues[low]);
    }

    // MC4: the 5 percentiles shown on the website and in the PDF
    public static PercentileSummary Percentiles(double[] values)
    {
        double[] sorted = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            sorted[i] = values[i];
        }
        Array.Sort(sorted);

        return new PercentileSummary(
            Percentile(sorted, 0.05),
            Percentile(sorted, 0.25),
            Percentile(sorted, 0.5),
            Percentile(sorted, 0.75),
            Percentile(sorted, 0.95));
    }

    // MC6: "binCount" intervals of the same width between the min and the max, the last one includes the max.
    // If all the values are equal, one single interval [min, max].
    public static HistogramBin[] Histogram(double[] values, int binCount)
    {
        double min = values[0];
        double max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        if (min == max)
        {
            return new HistogramBin[] { new HistogramBin(min, max, values.Length) };
        }

        // count the values in each interval
        double width = (max - min) / binCount;
        int[] counts = new int[binCount];
        for (int i = 0; i < values.Length; i++)
        {
            int index = (int)((values[i] - min) / width);
            if (index >= binCount)
            {
                index = binCount - 1;   // the max goes in the last interval
            }
            counts[index]++;
        }

        HistogramBin[] bins = new HistogramBin[binCount];
        for (int b = 0; b < binCount; b++)
        {
            double lower = min + b * width;
            double upper = min + (b + 1) * width;
            if (b == binCount - 1)
            {
                upper = max;
            }
            bins[b] = new HistogramBin(lower, upper, counts[b]);
        }
        return bins;
    }
}
