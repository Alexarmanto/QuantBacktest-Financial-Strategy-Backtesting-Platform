namespace QuantBacktest.Data.Tests.TestSupport;

/// <summary>A clock that only moves when the test says so.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
