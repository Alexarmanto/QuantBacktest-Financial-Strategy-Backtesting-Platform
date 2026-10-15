using Microsoft.EntityFrameworkCore;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.Storage.Entities;

namespace QuantBacktest.Data.Storage;

/// <summary>A cached series with its metadata.</summary>
internal sealed record CachedSeries(PriceSeries Series, DataOrigin Origin, DateTimeOffset LastFetchedUtc);

/// <summary>
/// Reads and merges cached price series (D3, D6, P1-D8, P1-D9). Symbols must already be normalised.
/// Serialising concurrent writers of the same series is the caller's job (P1-D11).
/// </summary>
internal sealed class PriceCache(SqliteDatabase database)
{
    /// <summary>The cached series, or null when nothing is cached for this symbol and timeframe.</summary>
    public async Task<CachedSeries?> GetAsync(string symbol, Timeframe timeframe, CancellationToken cancellationToken)
    {
        await using var context = await database.CreateContextAsync(cancellationToken);
        return await ReadAsync(context, symbol, timeframe, cancellationToken);
    }

    /// <summary>
    /// D3: inserts new dates and replaces existing ones; a cached bar is never deleted.
    /// P1-D9: the origin becomes Mixed as soon as bars of another origin are added.
    /// </summary>
    public async Task<CachedSeries> MergeAsync(
        string symbol,
        Timeframe timeframe,
        IReadOnlyList<PriceBar> bars,
        bool isAdjusted,
        DataOrigin origin,
        DateTimeOffset fetchedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(bars);
        if (bars.Count == 0)
        {
            throw new ArgumentException("At least one bar is required.", nameof(bars));
        }

        await using var context = await database.CreateContextAsync(cancellationToken);
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var existing = await context.PriceBars
                .Where(bar => bar.Symbol == symbol && bar.Timeframe == timeframe)
                .ToDictionaryAsync(bar => bar.Date, cancellationToken);

            foreach (var bar in bars)
            {
                if (existing.TryGetValue(bar.Date, out var entity))
                {
                    Copy(bar, isAdjusted, entity);
                }
                else
                {
                    entity = new PriceBarEntity { Symbol = symbol, Timeframe = timeframe, Date = bar.Date };
                    Copy(bar, isAdjusted, entity);
                    context.PriceBars.Add(entity);
                    existing.Add(bar.Date, entity);
                }
            }

            var metadata = await context.SeriesMetadata.FindAsync([symbol, timeframe], cancellationToken);
            if (metadata is null)
            {
                context.SeriesMetadata.Add(new SeriesMetadataEntity
                {
                    Symbol = symbol,
                    Timeframe = timeframe,
                    Origin = origin,
                    LastFetchedUtc = fetchedUtc,
                });
            }
            else
            {
                metadata.Origin = metadata.Origin == origin ? origin : DataOrigin.Mixed;
                metadata.LastFetchedUtc = fetchedUtc;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await ReadAsync(context, symbol, timeframe, cancellationToken)
            ?? throw new InvalidOperationException($"The merged series {symbol} ({timeframe}) could not be read back.");
    }

    /// <summary>P1-D12: distinct cached symbols, any timeframe, sorted (ordinal).</summary>
    public async Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        await using var context = await database.CreateContextAsync(cancellationToken);
        var symbols = await context.SeriesMetadata.Select(metadata => metadata.Symbol).Distinct().ToListAsync(cancellationToken);
        return [.. symbols.Order(StringComparer.Ordinal)];
    }

    private static async Task<CachedSeries?> ReadAsync(
        QuantBacktestDbContext context,
        string symbol,
        Timeframe timeframe,
        CancellationToken cancellationToken)
    {
        var entities = await context.PriceBars
            .AsNoTracking()
            .Where(bar => bar.Symbol == symbol && bar.Timeframe == timeframe)
            .OrderBy(bar => bar.Date)
            .ToListAsync(cancellationToken);
        if (entities.Count == 0)
        {
            return null;
        }

        var metadata = await context.SeriesMetadata
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Symbol == symbol && item.Timeframe == timeframe, cancellationToken)
            ?? throw new InvalidOperationException($"The cached series {symbol} ({timeframe}) has no metadata.");

        var bars = entities
            .Select(entity => new PriceBar(entity.Date, entity.Open, entity.High, entity.Low, entity.Close, entity.Volume))
            .ToList();

        // D6: a series is adjusted only when every one of its bars is.
        var series = new PriceSeries(symbol, timeframe, bars, entities.TrueForAll(entity => entity.IsAdjusted));
        return new CachedSeries(series, metadata.Origin, metadata.LastFetchedUtc);
    }

    private static void Copy(PriceBar bar, bool isAdjusted, PriceBarEntity entity)
    {
        entity.Open = bar.Open;
        entity.High = bar.High;
        entity.Low = bar.Low;
        entity.Close = bar.Close;
        entity.Volume = bar.Volume;
        entity.IsAdjusted = isAdjusted;
    }
}
