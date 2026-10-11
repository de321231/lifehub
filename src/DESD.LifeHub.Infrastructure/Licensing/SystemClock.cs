using DESD.LifeHub.Core.Abstractions;

namespace DESD.LifeHub.Infrastructure.Licensing;

/// <summary>Produktive Uhr (UTC).</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
