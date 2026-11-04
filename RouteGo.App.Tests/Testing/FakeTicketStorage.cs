using RouteGo.App.Common.Ports;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// Ticket storage as the tests need it: which bookings have a stored ticket, a storage that is out of
/// reach, and every link it made, so a test can see a fresh link per visit and that none was made
/// when none should be.
/// </summary>
public sealed class FakeTicketStorage : ITicketStorage
{
    public const string LinkHost = "https://tickets.test";

    private readonly HashSet<Guid> _stored = new HashSet<Guid>();
    private int _linksMade;

    public bool IsUnavailable { get; set; }

    public int Checks { get; private set; }

    /// <summary>The expiry time (as given to <see cref="MakeLink"/>) of every link made, oldest first.</summary>
    public List<DateTimeOffset> LinkTimes { get; } = new List<DateTimeOffset>();

    public void Store(Guid bookingId)
    {
        _stored.Add(bookingId);
    }

    public Task<TicketPresence> CheckAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        Checks++;
        if (IsUnavailable)
        {
            return Task.FromResult(TicketPresence.Unavailable);
        }

        if (_stored.Contains(bookingId))
        {
            return Task.FromResult(TicketPresence.Stored);
        }

        return Task.FromResult(TicketPresence.NotStored);
    }

    public string MakeLink(Guid bookingId, DateTimeOffset now)
    {
        _linksMade++;
        LinkTimes.Add(now);
        return $"{LinkHost}/tickets/{bookingId}.pdf?link={_linksMade}&expires={now.AddMinutes(ITicketStorage.LinkMinutes).ToUnixTimeSeconds()}";
    }
}
