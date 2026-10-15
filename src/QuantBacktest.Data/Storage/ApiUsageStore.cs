using Microsoft.EntityFrameworkCore;
using QuantBacktest.Data.Storage.Entities;

namespace QuantBacktest.Data.Storage;

/// <summary>Counts the HTTP requests sent to Alpha Vantage per UTC day, in the ApiUsage table (D4, D8, P1-D10).</summary>
internal sealed class ApiUsageStore(SqliteDatabase database)
{
    public async Task<int> GetCountAsync(DateOnly dayUtc, CancellationToken cancellationToken)
    {
        await using var context = await database.CreateContextAsync(cancellationToken);
        var usage = await context.ApiUsage.AsNoTracking().SingleOrDefaultAsync(item => item.DateUtc == dayUtc, cancellationToken);
        return usage?.Count ?? 0;
    }

    public async Task IncrementAsync(DateOnly dayUtc, CancellationToken cancellationToken)
    {
        await using var context = await database.CreateContextAsync(cancellationToken);
        var usage = await context.ApiUsage.FindAsync([dayUtc], cancellationToken);
        if (usage is null)
        {
            context.ApiUsage.Add(new ApiUsageEntity { DateUtc = dayUtc, Count = 1 });
        }
        else
        {
            usage.Count++;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
