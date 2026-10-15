namespace QuantBacktest.Data.Storage.Entities;

/// <summary>Table ApiUsage, key DateUtc: HTTP requests sent to Alpha Vantage that UTC day (P1-D8, P1-D10).</summary>
internal sealed class ApiUsageEntity
{
    public DateOnly DateUtc { get; set; }

    public int Count { get; set; }
}
