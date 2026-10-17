using QuantBacktest.Core.Models;

namespace QuantBacktest.Strategies.Tests;

// Builds small price series by hand for the tests
internal static class TestBars
{
    // one bar per business day from Monday 5 January 2026; open = high = low = close
    public static List<PriceBar> FromCloses(double[] closes)
    {
        List<PriceBar> bars = new List<PriceBar>();
        DateOnly date = new DateOnly(2026, 1, 5);
        for (int i = 0; i < closes.Length; i++)
        {
            decimal close = (decimal)closes[i];
            bars.Add(new PriceBar(date, close, close, close, close, 1000));
            date = NextBusinessDay(date);
        }
        return bars;
    }

    // same, but with a high and a low around the close
    public static List<PriceBar> FromCloses(double[] closes, double[] highs, double[] lows)
    {
        List<PriceBar> bars = new List<PriceBar>();
        DateOnly date = new DateOnly(2026, 1, 5);
        for (int i = 0; i < closes.Length; i++)
        {
            decimal close = (decimal)closes[i];
            bars.Add(new PriceBar(date, close, (decimal)highs[i], (decimal)lows[i], close, 1000));
            date = NextBusinessDay(date);
        }
        return bars;
    }

    // one bar per business day between two dates (both included), all at 100
    public static List<PriceBar> BusinessDays(DateOnly from, DateOnly to)
    {
        List<PriceBar> bars = new List<PriceBar>();
        DateOnly date = from;
        while (date <= to)
        {
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
            {
                bars.Add(new PriceBar(date, 100, 100, 100, 100, 1000));
            }
            date = date.AddDays(1);
        }
        return bars;
    }

    private static DateOnly NextBusinessDay(DateOnly date)
    {
        DateOnly next = date.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }
        return next;
    }
}
