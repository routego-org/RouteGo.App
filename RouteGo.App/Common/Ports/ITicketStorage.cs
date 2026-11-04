namespace RouteGo.App.Common.Ports;

/// <summary>Whether a booking's e-ticket is in the private ticket storage.</summary>
public enum TicketPresence
{
    Stored,

    /// <summary>Ticket Generator has not stored it (yet).</summary>
    NotStored,

    /// <summary>The storage could not be reached or refused the portal.</summary>
    Unavailable,
}

/// <summary>
/// Read-only access to the private storage Ticket Generator writes e-tickets to: one PDF per
/// booking, found by the booking's id. The portal only checks that a ticket is there and makes a
/// short-lived link to it.
/// </summary>
public interface ITicketStorage
{
    /// <summary>How long a link made by <see cref="MakeLink"/> keeps working.</summary>
    const int LinkMinutes = 5;

    Task<TicketPresence> CheckAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>A new link that opens the booking's ticket as <c>RouteGo-ticket-{bookingId}.pdf</c> until <see cref="LinkMinutes"/> after <paramref name="now"/>.</summary>
    string MakeLink(Guid bookingId, DateTimeOffset now);
}
