using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>
/// What every booking page does with the refusals it meets: the problem codes of Booking, and the
/// alerts for an unknown booking, someone else's booking, a booking that is not confirmed, booking
/// closed, and Booking being out of reach. The words come from the message catalog's booking-page
/// problem messages.
/// </summary>
public static class BookingProblems
{
    public const string NotFound = "NOT_FOUND";
    public const string BookingNotConfirmed = "BOOKING_NOT_CONFIRMED";
    public const string BookingClosed = "BOOKING_CLOSED";
    public const string TripCancelled = "TRIP_CANCELLED";
    public const string TripDeparted = "TRIP_DEPARTED";
    public const string SeatsUnavailable = "SEATS_UNAVAILABLE";
    public const string UnknownSeats = "UNKNOWN_SEATS";

    /// <summary>The alert for an unknown booking, or an address that is not a booking identifier.</summary>
    public static PageAlert NotFoundAlert()
    {
        var alert = PageAlert.Danger(Messages.ProblemNotFound("booking"));
        return alert.WithLink(BookingDisplay.LinkMyBookings, BookingDisplay.MyBookingsAddress);
    }

    /// <summary>
    /// The alert when a booking-page call is refused: unknown, someone else's, or any shared
    /// problem. An answer that is neither a refusal nor field errors cannot be told apart, so it
    /// is shown as an internal error with the reference support needs.
    /// </summary>
    public static PageAlert ToLoadAlert<T>(ApiResult<T> api, string correlationId)
    {
        ApiProblem? problem = api.Problem;
        if (problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(correlationId));
        }

        if (problem.Code == NotFound)
        {
            return NotFoundAlert();
        }

        if (problem.Code == SharedProblems.Forbidden)
        {
            var forbidden = PageAlert.Danger(Messages.ProblemForbidden);
            return forbidden.WithLink(BookingDisplay.LinkMyBookings, BookingDisplay.MyBookingsAddress);
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }

    /// <summary>
    /// Whether the portal itself could not reach Booking (or Booking did not answer in time), as
    /// opposed to Booking answering that a service of its own was down. The two are worded differently.
    /// </summary>
    public static bool IsUnreachable(ApiProblem? problem)
    {
        if (!IsOutage(problem))
        {
            return false;
        }

        return problem!.GetExtension("unreachable") == "true";
    }

    /// <summary>Whether a refusal says a service could not be reached or did not answer in time.</summary>
    public static bool IsOutage(ApiProblem? problem)
    {
        if (problem is null)
        {
            return false;
        }

        return problem.Code == SharedProblems.UpstreamUnavailable || problem.Code == SharedProblems.UpstreamTimeout;
    }

    /// <summary>"This booking is Cancelled, so this can't be done. Nothing was changed." with the way back to the booking.</summary>
    public static PageAlert NotConfirmedAlert(string bookingStatus, Guid bookingId)
    {
        return PageAlert.Danger(NotConfirmedText(bookingStatus)).WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId));
    }

    /// <summary>The words of a booking that is not confirmed, for a page with its own way back (the admin's).</summary>
    public static string NotConfirmedText(string bookingStatus)
    {
        return $"This booking is {BookingDisplay.StatusLabel(bookingStatus)}, so this can't be done. Nothing was changed.";
    }

    /// <summary>"This trip has already left. Nothing was changed." with the way back to the booking.</summary>
    public static PageAlert TripDepartedAlert(Guid bookingId)
    {
        return PageAlert.Danger(Messages.ProblemTripDeparted).WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId));
    }

    /// <summary>"This can't be done any more. The trip leaves at …, and booking and seat changes close 30 minutes before."</summary>
    public static PageAlert ClosedAlert(DateTimeOffset departureAt, int cutoffMinutes)
    {
        string departure = IndiaTime.Format(departureAt);
        string text = $"This can't be done any more. The trip leaves at {departure}, and booking and seat changes close {cutoffMinutes} minutes before. Nothing was changed.";
        return PageAlert.Danger(text);
    }

    /// <summary>The booking-closed alert from a refusal's departureAt and cutoffMinutes; the generic one when it names neither.</summary>
    public static PageAlert ClosedAlert(ApiProblem problem, string correlationId)
    {
        string? departureText = problem.GetExtension("departureAt");
        string? cutoffText = problem.GetExtension("cutoffMinutes");
        bool hasDeparture = DateTimeOffset.TryParse(departureText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset departureAt);
        bool hasCutoff = int.TryParse(cutoffText, NumberStyles.None, CultureInfo.InvariantCulture, out int cutoffMinutes);
        if (hasDeparture && hasCutoff)
        {
            return ClosedAlert(departureAt, cutoffMinutes);
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }
}
