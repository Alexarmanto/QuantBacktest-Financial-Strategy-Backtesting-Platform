using QuantBacktest.Core.Models;

namespace QuantBacktest.Data.Storage.Entities;

/// <summary>Table SeriesMetadata, key (Symbol, Timeframe) (P1-D8).</summary>
internal sealed class SeriesMetadataEntity
{
    public required string Symbol { get; set; }

    public Timeframe Timeframe { get; set; }

    public DataOrigin Origin { get; set; }

    public DateTimeOffset LastFetchedUtc { get; set; }
}
