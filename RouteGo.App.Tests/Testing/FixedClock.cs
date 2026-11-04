using RouteGo.App.Common.Ports;

namespace RouteGo.App.Tests.Testing;

/// <summary>A clock that always reads the same moment, so token expiry is decided by the test.</summary>
public sealed class FixedClock : IClock
{
    /// <summary>The moment every renewal test starts from.</summary>
    public static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow
    {
        get { return Now; }
    }
}
