using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Fakes;

namespace QuantBacktest.Strategies.Tests;

// X3, S3: a strategy must never look at the future.
// If we cut the series at bar k, the results up to k must be exactly the same as with the whole series.
public class NoLookAheadTests
{
    private static readonly Dictionary<string, double> Defaults = new Dictionary<string, double>();
    private static readonly int[] CutBars = new int[] { 1, 2, 30, 120, 300, 499 };

    public static TheoryData<string, int> IdsAndSeeds()
    {
        TheoryData<string, int> data = new TheoryData<string, int>();
        foreach (IStrategy strategy in TestStrategies.All())
        {
            data.Add(strategy.Id, 1);
            data.Add(strategy.Id, 7);
            data.Add(strategy.Id, 42);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(IdsAndSeeds))]
    public void SignalsAndIndicators_DoNotChange_WhenTheFutureIsRemoved(string id, int seed) // X3, S3, S6
    {
        IStrategy strategy = TestStrategies.Find(id);
        IReadOnlyList<PriceBar> allBars = SyntheticPrices.Generate("TEST", Timeframe.Daily, 500, seed).Bars;
        IReadOnlyList<Signal> allSignals = strategy.GenerateSignals(allBars, Defaults);
        IReadOnlyList<IndicatorSeries> allIndicators = strategy.ComputeIndicators(allBars, Defaults);

        foreach (int k in CutBars)
        {
            // only bars[0..k]
            List<PriceBar> pastBars = new List<PriceBar>();
            for (int i = 0; i <= k; i++)
            {
                pastBars.Add(allBars[i]);
            }

            IReadOnlyList<Signal> pastSignals = strategy.GenerateSignals(pastBars, Defaults);
            IReadOnlyList<IndicatorSeries> pastIndicators = strategy.ComputeIndicators(pastBars, Defaults);

            for (int i = 0; i <= k; i++)
            {
                pastSignals[i].Should().Be(allSignals[i], $"signal of bar {i} changed when cutting at bar {k}");
            }

            pastIndicators.Count.Should().Be(allIndicators.Count);
            for (int s = 0; s < allIndicators.Count; s++)
            {
                for (int i = 0; i <= k; i++)
                {
                    pastIndicators[s].Values[i].Should().Be(allIndicators[s].Values[i], $"{allIndicators[s].Key} at bar {i} changed when cutting at bar {k}");
                }
            }
        }
    }
}
