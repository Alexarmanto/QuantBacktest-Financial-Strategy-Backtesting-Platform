using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QuantBacktest.Core.Abstractions;
using QuantBacktest.Core.Models;
using QuantBacktest.Data.Storage;
using QuantBacktest.Data.Storage.Entities;

namespace QuantBacktest.Data;

/// <summary>
/// Part 1 implementation of <see cref="ICustomStrategyStore"/> (P1-D14): one JSON document per definition in the
/// CustomStrategies table. Stores and reads back faithfully; validating the rules is Part 2's job, done before SaveAsync.
/// </summary>
internal sealed class SqliteCustomStrategyStore(SqliteDatabase database, TimeProvider time) : ICustomStrategyStore, IDisposable
{
    /// <summary>P1-D14: camelCase, enums as text. Read-only once used.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>Sorted by Name, ordinal and case-insensitive.</summary>
    public async Task<IReadOnlyList<CustomStrategyDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await database.CreateContextAsync(cancellationToken);
        var entities = await context.CustomStrategies.AsNoTracking().ToListAsync(cancellationToken);
        return [.. entities.OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase).Select(Deserialize)];
    }

    public async Task<CustomStrategyDefinition?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await using var context = await database.CreateContextAsync(cancellationToken);
        var entity = await context.CustomStrategies.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return entity is null ? null : Deserialize(entity);
    }

    /// <summary>Inserts, or replaces the definition with the same Id. No validation (P1-D14).</summary>
    public async Task SaveAsync(CustomStrategyDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.Serialize(definition, JsonOptions);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await database.CreateContextAsync(cancellationToken);
            var entity = await context.CustomStrategies.FindAsync([definition.Id], cancellationToken);
            if (entity is null)
            {
                context.CustomStrategies.Add(new CustomStrategyEntity
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    Json = json,
                    UpdatedUtc = time.GetUtcNow(),
                });
            }
            else
            {
                entity.Name = definition.Name;
                entity.Json = json;
                entity.UpdatedUtc = time.GetUtcNow();
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>False when the id is unknown.</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var context = await database.CreateContextAsync(cancellationToken);
            return await context.CustomStrategies.Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose() => _writeLock.Dispose();

    private static CustomStrategyDefinition Deserialize(CustomStrategyEntity entity)
        => JsonSerializer.Deserialize<CustomStrategyDefinition>(entity.Json, JsonOptions)
            ?? throw new InvalidOperationException($"The stored custom strategy '{entity.Id}' is empty.");
}
