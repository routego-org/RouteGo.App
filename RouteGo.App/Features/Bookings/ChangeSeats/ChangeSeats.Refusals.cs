namespace RouteGo.App.Features.Bookings.ChangeSeats;

/// <summary>
/// The words for everything that stops a seat change: the booking not being open to changes when
/// the page loads, the seat map not being available, and each refusal Booking can answer to a sent
/// change. After every refusal Booking changed nothing, so the page loads the booking and seats again.
/// </summary>
public static class ChangeSeatsRefusals
{
    private const string SeatChangeLimitReached = "SEAT_CHANGE_LIMIT_REACHED";
    private const string SeatsNotInBooking = "SEATS_NOT_IN_BOOKING";
    private const string SeatClassMismatch = "SEAT_CLASS_MISMATCH";
    private const string NothingChanged = "Nothing was changed.";

    /// <summary>
    /// Why a booking cannot be changed, worked out from the booking as loaded; null when it can.
    /// Not confirmed comes first, then a trip that has left, then the last 30 minutes, then all
    /// seat changes used.
    /// </summary>
    public static PageAlert? NotOpenAlert(BookingDetail booking, DateTimeOffset now)
    {
        if (booking.Status != BookingDisplay.Confirmed)
        {
            return BookingProblems.NotConfirmedAlert(booking.Status, booking.BookingId);
        }

        DateTimeOffset departureAt = booking.Trip.DepartureAt;
        if (departureAt <= now)
        {
            return BookingProblems.TripDepartedAlert(booking.BookingId);
        }

        if (departureAt - now < TimeSpan.FromMinutes(BookingDisplay.CutoffMinutes))
        {
            PageAlert closed = BookingProblems.ClosedAlert(departureAt, BookingDisplay.CutoffMinutes);
            return closed.WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(booking.BookingId));
        }

        if (booking.SeatChangeCount >= BookingDisplay.MaxSeatChanges)
        {
            return LimitAlert(booking.BookingId, BookingDisplay.MaxSeatChanges.ToString(), false);
        }

        return null;
    }

    /// <summary>The message when the seat map says the trip is cancelled or has left; null for a trip that can still be changed.</summary>
    public static PageAlert? TripAlert(TripSeatMap map, Guid bookingId)
    {
        if (map.IsCancelled)
        {
            return PageAlert.Danger(Messages.ProblemTripCancelled).WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId));
        }

        if (map.IsDeparted)
        {
            return BookingProblems.TripDepartedAlert(bookingId);
        }

        return null;
    }

    /// <summary>The message when the seat map could not be loaded: not available (with "Try again"), or a shared problem.</summary>
    public static PageAlert SeatMapAlert<T>(ApiResult<T> map, Guid bookingId, string correlationId)
    {
        if (SeatDisplay.IsUnavailable(map))
        {
            var alert = PageAlert.Danger(Messages.ProblemSeatDetailsUnavailable);
            alert = alert.WithLink(Messages.LinkTryAgain, $"{BookingDisplay.BookingAddress(bookingId)}/change-seats");
            return alert.WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId));
        }

        return BookingProblems.ToLoadAlert(map, correlationId);
    }

    /// <summary>What Booking's refusal of a sent change comes to on the page.</summary>
    public static ChangeOutcome ToOutcome<T>(ApiResult<T> api, Guid bookingId, string correlationId)
    {
        ApiProblem? problem = api.Problem;
        if (problem is null)
        {
            return Reload(PageAlert.Danger(FirstFieldMessage(api)));
        }

        if (BookingProblems.IsUnreachable(problem))
        {
            return new ChangeOutcome { Alert = PageAlert.Danger(Messages.ProblemUpstream) };
        }

        PageAlert? alert = BookingRefusal(problem, bookingId, correlationId);
        if (alert is null)
        {
            // Unknown booking, someone else's, or a problem no rule names: nothing of the booking is shown.
            return new ChangeOutcome { Alert = BookingProblems.ToLoadAlert(api, correlationId) };
        }

        return Reload(alert);
    }

    // The refusals Booking names for a sent change; null for any other code.
    private static PageAlert? BookingRefusal(ApiProblem problem, Guid bookingId, string correlationId)
    {
        switch (problem.Code)
        {
            case BookingProblems.SeatsUnavailable:
                return PageAlert.Danger(TakenText(problem));
            case SeatClassMismatch:
                return PageAlert.Danger($"A seat can only be changed for one of the same class. {NothingChanged}");
            case SeatsNotInBooking:
            case BookingProblems.UnknownSeats:
                return PageAlert.Danger($"Some seats do not match this booking. We loaded it again. {NothingChanged}");
            case SeatChangeLimitReached:
                return LimitAlert(bookingId, problem.GetExtension("maxSeatChanges") ?? BookingDisplay.MaxSeatChanges.ToString(), true);
            case BookingProblems.BookingClosed:
                return BookingProblems.ClosedAlert(problem, correlationId).WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId));
            case BookingProblems.TripCancelled:
                return PageAlert.Danger(Messages.ProblemTripCancelled);
            case BookingProblems.TripDeparted:
                return BookingProblems.TripDepartedAlert(bookingId);
            case BookingProblems.BookingNotConfirmed:
                return BookingProblems.NotConfirmedAlert(problem.GetExtension("bookingStatus") ?? string.Empty, bookingId);
            case SharedProblems.UpstreamUnavailable:
                return PageAlert.Danger("We could not change your seats. Your booking still has its old seats. Please try again.");
            case SharedProblems.UpstreamTimeout:
                return PageAlert.Danger("The change is taking longer than usual. Your booking still shows your old seats. Please check it in a few minutes before trying again.");
            default:
                return null;
        }
    }

    private static string TakenText(ApiProblem problem)
    {
        List<string> seatNos = SeatDisplay.ExtensionSeats(problem, "unavailableSeats");
        string verb = "was";
        if (seatNos.Count > 1)
        {
            verb = "were";
        }

        return $"{SeatDisplay.SeatList(seatNos)} {verb} just taken. {NothingChanged} We loaded the seats again.";
    }

    private static PageAlert LimitAlert(Guid bookingId, string maxSeatChanges, bool isAfterRefusal)
    {
        string text = $"You have used all {maxSeatChanges} seat changes for this booking.";
        if (isAfterRefusal)
        {
            text = $"{text} {NothingChanged}";
        }

        return PageAlert.Danger(text).WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId));
    }

    private static string FirstFieldMessage<T>(ApiResult<T> api)
    {
        if (api.Errors is null)
        {
            return Messages.ProblemUpstream;
        }

        foreach (string[] messages in api.Errors.Values)
        {
            if (messages.Length > 0)
            {
                return messages[0];
            }
        }

        return Messages.ProblemUpstream;
    }

    private static ChangeOutcome Reload(PageAlert alert)
    {
        return new ChangeOutcome { Alert = alert, ShouldReload = true };
    }
}
