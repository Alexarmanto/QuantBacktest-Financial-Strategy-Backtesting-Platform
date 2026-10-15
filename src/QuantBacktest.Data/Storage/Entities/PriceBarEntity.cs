using QuantBacktest.Core.Models;

namespace QuantBacktest.Data.Storage.Entities;

/// <summary>Table PriceBars, key (Symbol, Timeframe, Date) (P1-D8).</summary>
internal sealed class PriceBarEntity
{
    public required string Symbol { get; set; }

    public Timeframe Timeframe { get; set; }

    public DateOnly Date { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    public long Volume { get; set; }

    public bool IsAdjusted { get; set; }
}
