namespace RouteGo.App.Features.Admin.SeatAllocations;

/// <summary>A booking's seats still On hold although their hold ended more than 5 minutes ago.</summary>
public sealed record StuckHold(Guid BookingId, IReadOnlyList<string> SeatNos);

/// <summary>
/// Which holds the page treats as stuck, and the spec's words for cancelling one. A hold that is
/// still running, or ended 5 minutes ago or less, is never offered for cancelling, so a customer
/// who is still paying is never cut off — although Seat Inventory would allow it.
/// </summary>
public static class StuckHolds
{
    /// <summary>How long after its end a hold still on its seats counts as stuck.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private const string HoldNotActive = "HOLD_NOT_ACTIVE";
    private const string HoldAlreadyConfirmed = "HOLD_ALREADY_CONFIRMED";

    /// <summary>Whether a seat is held by a hold that ended more than 5 minutes before <paramref name="now"/>.</summary>
    public static bool IsStuck(StaffSeat seat, DateTimeOffset now)
    {
        return seat.Status == SeatDisplay.Held && seat.HoldExpiresAt != null && seat.HoldExpiresAt.Value < now - Grace;
    }

    /// <summary>The stuck holds on the trip, one per booking, in seat order.</summary>
    public static List<StuckHold> Find(IReadOnlyList<StaffSeat> seats, DateTimeOffset now)
    {
        var bookings = new List<Guid>();
        var seatsByBooking = new Dictionary<Guid, List<string>>();
        foreach (StaffSeat seat in seats)
        {
            if (!IsStuck(seat, now) || !Guid.TryParse(seat.BookingId, out Guid bookingId))
            {
                continue;
            }

            if (!seatsByBooking.ContainsKey(bookingId))
            {
                bookings.Add(bookingId);
                seatsByBooking[bookingId] = new List<string>();
            }

            seatsByBooking[bookingId].Add(seat.SeatNo);
        }

        return bookings.Select(bookingId => new StuckHold(bookingId, seatsByBooking[bookingId])).ToList();
    }

    /// <summary>"1 stuck hold on this trip." / "2 stuck holds on this trip."</summary>
    public static string CountText(int count)
    {
        string holds = count == 1 ? "hold" : "holds";
        return $"{count} stuck {holds} on this trip.";
    }

    /// <summary>"Cancel the hold of booking {id} on seats 3A, 3B? {what happens} This cannot be undone."</summary>
    public static string Question(StuckHold hold, StaffSeatList trip)
    {
        string seats = SeatDisplay.SeatList(hold.SeatNos);
        return $"Cancel the hold of booking {hold.BookingId} on seats {seats}? {Outcome(trip)} This cannot be undone.";
    }

    /// <summary>What the success flash says, by what happens to the freed seats on this trip.</summary>
    public static string Cancelled(IReadOnlyList<string> releasedSeatNos, StaffSeatList trip)
    {
        string seats = SeatDisplay.SeatList(releasedSeatNos);
        if (trip.IsCancelled)
        {
            return $"Hold cancelled. Seats {seats} are now blocked because the trip was cancelled.";
        }

        if (trip.IsDeparted)
        {
            return $"Hold cancelled. Seats {seats} are free again.";
        }

        return $"Hold cancelled. Seats {seats} are back on sale.";
    }

    /// <summary>The alert for a cancellation refused because the hold moved on; null for any other refusal.</summary>
    public static PageAlert? HoldStateAlert(ApiProblem problem, Guid bookingId)
    {
        if (problem.Code == HoldNotActive)
        {
            return PageAlert.Danger("This booking no longer holds any seats on this trip. Nothing was changed.");
        }

        if (problem.Code == HoldAlreadyConfirmed)
        {
            string seats = SeatDisplay.SeatList(SeatDisplay.ExtensionSeats(problem, "seatNos"));
            var alert = PageAlert.Danger($"The customer has paid for seats {seats}. Nothing was changed. A paid booking can only be cancelled through Booking.");
            return alert.WithLink($"Open booking {bookingId}", $"/admin/bookings/{bookingId}");
        }

        return null;
    }

    // A cancelled trip's seats never return to sale; a departed trip's are simply free again.
    private static string Outcome(StaffSeatList trip)
    {
        if (trip.IsCancelled)
        {
            return "The trip was cancelled, so the seats will be blocked.";
        }

        if (trip.IsDeparted)
        {
            return "The seats will be free again.";
        }

        return "The seats go back on sale.";
    }
}
