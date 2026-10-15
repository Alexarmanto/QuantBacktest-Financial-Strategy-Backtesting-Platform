using Microsoft.EntityFrameworkCore;
using QuantBacktest.Data.Storage.Entities;

namespace QuantBacktest.Data.Storage;

/// <summary>
/// The local SQLite cache (P1-D8). Created with EnsureCreated, no migrations.
/// The SQLite provider can neither sort nor aggregate decimal columns: queries only sort by Date; computations run in memory.
/// </summary>
internal sealed class QuantBacktestDbContext(DbContextOptions<QuantBacktestDbContext> options) : DbContext(options)
{
    public DbSet<PriceBarEntity> PriceBars => Set<PriceBarEntity>();

    public DbSet<SeriesMetadataEntity> SeriesMetadata => Set<SeriesMetadataEntity>();

    public DbSet<ApiUsageEntity> ApiUsage => Set<ApiUsageEntity>();

    public DbSet<CustomStrategyEntity> CustomStrategies => Set<CustomStrategyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PriceBarEntity>(entity =>
        {
            entity.ToTable("PriceBars");
            entity.HasKey(bar => new { bar.Symbol, bar.Timeframe, bar.Date });
            entity.Property(bar => bar.Timeframe).HasConversion<string>();
        });

        modelBuilder.Entity<SeriesMetadataEntity>(entity =>
        {
            entity.ToTable("SeriesMetadata");
            entity.HasKey(metadata => new { metadata.Symbol, metadata.Timeframe });
            entity.Property(metadata => metadata.Timeframe).HasConversion<string>();
            entity.Property(metadata => metadata.Origin).HasConversion<string>();
        });

        modelBuilder.Entity<ApiUsageEntity>(entity =>
        {
            entity.ToTable("ApiUsage");
            entity.HasKey(usage => usage.DateUtc);
        });

        modelBuilder.Entity<CustomStrategyEntity>(entity =>
        {
            entity.ToTable("CustomStrategies");
            entity.HasKey(strategy => strategy.Id);
        });
    }
}
