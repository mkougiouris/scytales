namespace Scytales.Infrastructure.Persistence;

/// <summary>
/// The application clock, truncated to the microsecond precision of PostgreSQL's <c>timestamptz</c>. Without it,
/// a response built from the in-memory aggregate (e.g. approve) would show 100 ns ticks that a later read of
/// the same row no longer has.
/// </summary>
internal sealed class StoragePrecisionTimeProvider(TimeProvider inner) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        var now = inner.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override long GetTimestamp() => inner.GetTimestamp();
}
