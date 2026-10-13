using QuantBacktest.Core.Models;

namespace QuantBacktest.Fakes;

/// <summary>
/// Deterministic synthetic OHLCV generator (geometric random walk). Same arguments always give the same series.
/// Use it in tests and demos. Never present results computed on it as real market results.
/// </summary>
public static class SyntheticPrices
{
    public static PriceSeries Generate(
        string symbol,
        Timeframe timeframe = Timeframe.Daily,
        int barCount = 1000,
        int seed = 42,
        DateOnly? start = null,
        decimal startPrice = 100m)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        if (barCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(barCount), "At least 2 bars are required.");
        }

        var rng = new Random(seed);
        var sigma = timeframe == Timeframe.Daily ? 0.012 : 0.027;
        var drift = timeframe == Timeframe.Daily ? 0.0004 : 0.0018;
        var date = Align(start ?? new DateOnly(2018, 1, 1), timeframe);
        var previousClose = (double)startPrice;
        var bars = new List<PriceBar>(barCount);

        for (var i = 0; i < barCount; i++)
        {
            var open = previousClose * (1 + (0.002 * NextGaussian(rng)));
            var close = open * Math.Exp(drift + (sigma * NextGaussian(rng)));
            var high = Math.Max(open, close) * (1 + Math.Abs(0.004 * NextGaussian(rng)));
            var low = Math.Min(open, close) * (1 - Math.Abs(0.004 * NextGaussian(rng)));

            var o = ToPrice(open);
            var c = ToPrice(close);
            var h = Math.Max(ToPrice(high), Math.Max(o, c));
            var l = Math.Min(ToPrice(low), Math.Min(o, c));
            var volume = 1_000_000L + rng.Next(0, 5_000_000);

            bars.Add(new PriceBar(date, o, h, l, c, volume));
            previousClose = (double)c;
            date = Next(date, timeframe);
        }

        return new PriceSeries(symbol.Trim().ToUpperInvariant(), timeframe, bars, IsAdjusted: true);
    }

    private static decimal ToPrice(double value) => Math.Max(0.01m, Math.Round((decimal)value, 2));

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static DateOnly Align(DateOnly date, Timeframe timeframe)
    {
        if (timeframe == Timeframe.Weekly)
        {
            while (date.DayOfWeek != DayOfWeek.Friday)
            {
                date = date.AddDays(1);
            }

            return date;
        }

        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static DateOnly Next(DateOnly date, Timeframe timeframe)
    {
        if (timeframe == Timeframe.Weekly)
        {
            return date.AddDays(7);
        }

        var next = date.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }

        return next;
    }
}
