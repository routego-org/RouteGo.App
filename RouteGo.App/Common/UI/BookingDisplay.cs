
namespace RouteGo.App.Common.UI;

/// <summary>
/// The rules every booking page shares, written once: the words for a booking's status, failure,
/// refund and cancellation, the hold countdown and the addresses of the booking pages. The words
/// come from the message catalog's "Booking labels". What a page says about a refusal is in
/// <see cref="BookingProblems"/>.
/// </summary>
public static class BookingDisplay
{
    public const string Initiated = "initiated";
    public const string SeatsHeld = "seatsHeld";
    public const string PaymentPending = "paymentPending";
    public const string Confirmed = "confirmed";
    public const string Failed = "failed";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    public const string MyBookingsAddress = "/my-bookings";

    /// <summary>The most seats one booking can hold; Booking's limit.</summary>
    public const int MaxSeats = 6;

    /// <summary>How many seat changes one booking may have; Booking's limit.</summary>
    public const int MaxSeatChanges = 3;

    /// <summary>
    /// Minutes before departure at which seat changes close. Booking decides and refuses with the
    /// value it holds; this one only decides whether the booking page offers the action at all.
    /// </summary>
    public const int CutoffMinutes = 30;

    public const string LinkMyBookings = "My bookings";
    public const string LinkViewBooking = "View booking";
    public const string LinkViewTicket = "View ticket";
    public const string LinkSearchBuses = "Search for buses";
    public const string ExpiredNote = "The payment did not arrive in time, so the seats were released. A payment that arrives late is refunded in full.";
    public const string NoRefundDue = "No refund is due.";

    /// <summary>The booking page of a booking, which every other booking page links back to.</summary>
    public static string BookingAddress(Guid bookingId)
    {
        return $"/my-bookings/{bookingId}";
    }

    public static string TicketAddress(Guid bookingId)
    {
        return $"/tickets/{bookingId}";
    }

    /// <summary>The booking page of a trip, passing the route label on the way the seat map link does.</summary>
    public static string BookSeatsAddress(Guid tripId, string? route)
    {
        return SeatDisplay.WithRoute($"/trips/{tripId}/book", route);
    }

    /// <summary>
    /// The payment page address Booking gave, when it is one a browser may be sent to: an absolute
    /// http or https address. Anything else is dropped, so a page never redirects somewhere odd.
    /// </summary>
    public static string? PaymentAddress(string? checkoutUrl)
    {
        bool isAbsolute = Uri.TryCreate(checkoutUrl, UriKind.Absolute, out Uri? address);
        bool isWeb = isAbsolute && (address!.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);
        if (!isWeb)
        {
            return null;
        }

        return address!.AbsoluteUri;
    }

    /// <summary>The page a booking address names; null when the text is not a booking identifier.</summary>
    public static Guid? ParseBookingId(string? text)
    {
        if (!Guid.TryParse(text, out Guid bookingId))
        {
            return null;
        }

        return bookingId;
    }

    /// <summary>Whether Booking has not yet had the payment: initiated, seats held or payment pending.</summary>
    public static bool IsWaiting(string status)
    {
        bool isFinal = status == Confirmed || status == Failed || status == Expired || status == Cancelled;
        return !isFinal;
    }

    /// <summary>state.waiting_for_payment, state.confirmed, state.failed, state.expired or state.cancelled.</summary>
    public static string StatusLabel(string status)
    {
        switch (status)
        {
            case Confirmed:
                return "Confirmed";
            case Failed:
                return "Failed";
            case Expired:
                return "Expired";
            case Cancelled:
                return Messages.StateCancelled;
            default:
                return "Waiting for payment";
        }
    }

    /// <summary>gender.male, gender.female or gender.other.</summary>
    public static string GenderLabel(string gender)
    {
        switch (gender)
        {
            case "male":
                return "Male";
            case "female":
                return "Female";
            default:
                return "Other";
        }
    }

    /// <summary>The booking_failure.* words for a failure reason as Booking spells it.</summary>
    public static string FailureLabel(string reason)
    {
        switch (reason)
        {
            case "seatsUnavailable":
                return "A seat was taken by someone else first.";
            case "inventoryUnavailable":
                return "RouteGo could not hold the seats. No payment was started.";
            case "paymentUnavailable":
                return "RouteGo could not start the payment. The seats were released.";
            case "paymentDeclined":
                return "The payment did not go through.";
            case "abandoned":
                return "The booking was given up before payment.";
            case "tripCancelled":
                return "The trip was cancelled. If a payment was made, it is refunded in full.";
            default:
                return "The seats were no longer held when the payment arrived. If a payment was made, it is refunded in full.";
        }
    }

    /// <summary>cancellation_kind.* for who cancelled.</summary>
    public static string CancellationKindLabel(string kind)
    {
        switch (kind)
        {
            case "adminCancelled":
                return "Cancelled by RouteGo";
            case "operatorCancelled":
                return "Cancelled by the bus company";
            default:
                return "Cancelled by the customer";
        }
    }

    /// <summary>refund_reason.* for why money goes back.</summary>
    public static string RefundReasonLabel(string reason)
    {
        switch (reason)
        {
            case "latePayment":
                return "Late payment";
            case "seatsLost":
                return "Seats no longer held";
            case "tripCancelled":
                return "Trip cancelled";
            default:
                return "Cancellation";
        }
    }

    /// <summary>refund_status.requested or refund_status.completed.</summary>
    public static string RefundStatusLabel(string status)
    {
        if (status == "completed")
        {
            return "Refund completed";
        }

        return "Refund requested";
    }

    /// <summary>"Cancellation · ₹989 · Refund completed".</summary>
    public static string RefundLine(BookingRefundInfo refund)
    {
        string reason = RefundReasonLabel(refund.Reason);
        string amount = TripDisplay.Rupees(refund.Amount);
        return $"{reason} · {amount} · {RefundStatusLabel(refund.Status)}";
    }

    /// <summary>"{source} → {destination}".</summary>
    public static string RouteName(BookingTrip trip)
    {
        return $"{trip.SourceCityName} → {trip.DestinationCityName}";
    }

    /// <summary>"Mumbai → Pune, 30 Oct 2026, 9:00 PM IST", the tail of a booking page's title.</summary>
    public static string RouteAndDeparture(BookingTrip trip)
    {
        return $"{RouteName(trip)}, {IndiaTime.Format(trip.DepartureAt)}";
    }

    /// <summary>The whole seconds left of a hold; null when Booking gives no hold end or it has passed.</summary>
    public static int? HoldSecondsLeft(DateTimeOffset? holdExpiresAt, DateTimeOffset now)
    {
        if (holdExpiresAt is null)
        {
            return null;
        }

        double seconds = (holdExpiresAt.Value - now).TotalSeconds;
        if (seconds <= 0)
        {
            return null;
        }

        return (int)Math.Ceiling(seconds);
    }

    /// <summary>"6 minutes 12 seconds"; the script on the page writes the same words each second.</summary>
    public static string TimeLeftText(int secondsLeft)
    {
        int minutes = secondsLeft / 60;
        int seconds = secondsLeft % 60;
        return $"{Counted(minutes, "minute")} {Counted(seconds, "second")}";
    }

    /// <summary>The facts about how a booking ended, in the order a person reads them; none for a booking still waiting or confirmed.</summary>
    public static IReadOnlyList<OutcomeLine> OutcomeLines(BookingDetail booking)
    {
        var lines = new List<OutcomeLine>();
        if (booking.Status == Failed)
        {
            lines.Add(new OutcomeLine("failure", FailureLabel(booking.FailureReason ?? string.Empty)));
            AddPaymentDetail(lines, booking.PaymentFailureDetail);
        }

        if (booking.Status == Expired)
        {
            lines.Add(new OutcomeLine("note", ExpiredNote));
        }

        if (booking.Cancellation != null)
        {
            AddCancellation(lines, booking.Cancellation);
        }

        if (booking.Refund != null)
        {
            lines.Add(new OutcomeLine("refund", RefundLine(booking.Refund)));
        }

        return lines;
    }

    private static void AddPaymentDetail(List<OutcomeLine> lines, string? detail)
    {
        if (!string.IsNullOrWhiteSpace(detail))
        {
            lines.Add(new OutcomeLine("payment-detail", $"The payment service said: {detail}"));
        }
    }

    private static void AddCancellation(List<OutcomeLine> lines, BookingCancellationInfo cancellation)
    {
        lines.Add(new OutcomeLine("kind", CancellationKindLabel(cancellation.Kind)));
        if (!string.IsNullOrWhiteSpace(cancellation.Reason))
        {
            lines.Add(new OutcomeLine("reason", $"Reason: {cancellation.Reason}"));
        }

        lines.Add(new OutcomeLine("cancelled-at", $"Cancelled at: {IndiaTime.Format(cancellation.CancelledAt)}"));
        lines.Add(new OutcomeLine("percent", $"Refund percentage: {cancellation.RefundPercent}%"));
        if (cancellation.RefundAmount == 0)
        {
            lines.Add(new OutcomeLine("no-refund", NoRefundDue));
        }
    }

    private static string Counted(int count, string unit)
    {
        if (count == 1)
        {
            return $"1 {unit}";
        }

        return $"{count} {unit}s";
    }

    /// <summary>One line about how a booking ended; the key lets a page and a test find it.</summary>
    public sealed record OutcomeLine(string Key, string Text);
}
