using System.Net;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// One booking as Booking holds it, changeable with <c>with</c>. Defaults are the specs' example:
/// Priya's confirmed booking of seats 3A and 3B (₹800 each) on trip T1, Mumbai → Pune, leaving at
/// 9:00 PM IST on the day of the test clock — 5 hours 30 minutes after it.
/// </summary>
public sealed record BookingStub
{
    public Guid BookingId { get; init; } = BookingStubs.BookingId;

    public string Status { get; init; } = "confirmed";

    public string DepartureAt { get; init; } = "2026-10-02T15:30:00Z";

    public string? HoldExpiresAt { get; init; }

    public string? CheckoutUrl { get; init; }

    public string? FailureReason { get; init; }

    public string? PaymentFailureDetail { get; init; }

    public int SeatChangeCount { get; init; }

    public string? ConfirmedAt { get; init; } = "2026-10-01T05:02:00Z";

    public object? Cancellation { get; init; }

    public object? Refund { get; init; }

    /// <summary>Booking's Booking shape.</summary>
    public object ToWire()
    {
        return new
        {
            bookingId = BookingId,
            status = Status,
            trip = Trip(),
            contactName = "Priya Shah",
            contactEmail = "priya@example.com",
            seats = Seats(),
            totalAmount = 1600,
            currency = "INR",
            holdExpiresAt = HoldExpiresAt,
            checkoutUrl = CheckoutUrl,
            failureReason = FailureReason,
            paymentFailureDetail = PaymentFailureDetail,
            seatChangeCount = SeatChangeCount,
            createdAt = "2026-10-01T05:00:00Z",
            confirmedAt = ConfirmedAt,
            cancellation = Cancellation,
            refund = Refund,
        };
    }

    /// <summary>Booking's TripSummary shape.</summary>
    public object Trip()
    {
        return new
        {
            tripId = BookingStubs.TripId,
            sourceCityName = "Mumbai",
            destinationCityName = "Pune",
            serviceDate = "2026-10-02",
            departureAt = DepartureAt,
            arrivalAt = "2026-10-02T21:00:00Z",
            busName = "Shivneri Sleeper",
            busType = "sleeper",
            operatorName = "BusCo Travels",
        };
    }

    /// <summary>The two booked seats with their passengers.</summary>
    public static object[] Seats()
    {
        return new object[]
        {
            new { seatNo = "3A", seatClass = "Seater", fare = 800, passengerName = "Priya Shah", passengerAge = 34, passengerGender = "female" },
            new { seatNo = "3B", seatClass = "Seater", fare = 800, passengerName = "Ravi Shah", passengerAge = 36, passengerGender = "male" },
        };
    }

    public HttpResponseMessage Answer()
    {
        return FakeBackend.Json(ToWire());
    }
}

/// <summary>Booking's addresses and answers the booking page tests share.</summary>
public static class BookingStubs
{
    public static readonly Guid BookingId = Guid.Parse("77777777-0000-4000-8000-000000000077");
    public static readonly Guid TripId = SeatStubs.TripId;

    public static readonly string BookingPath = $"/bookings/{BookingId}";
    public static readonly string BookingsPath = "/bookings";
    public static readonly string ReleasePath = $"/bookings/{BookingId}/release";
    public static readonly string SeatChangesPath = $"/bookings/{BookingId}/seatChanges";
    public static readonly string PreviewPath = $"/bookings/{BookingId}/cancellationPreview";
    public static readonly string CancelPath = $"/bookings/{BookingId}/cancel";

    public static readonly string PageAddress = $"/my-bookings/{BookingId}";
    public static readonly string PaymentReturnAddress = $"/my-bookings/{BookingId}/payment-return";

    /// <summary>A Booking refusal naming extension members (unavailableSeats, currentTotal, bookingStatus…).</summary>
    public static HttpResponseMessage Refusal(HttpStatusCode status, string code, Dictionary<string, object>? extensions = null)
    {
        return IdentityStubs.Problem(status, code, extensions);
    }

    /// <summary>A refusal that carries one extension member.</summary>
    public static HttpResponseMessage Refusal(HttpStatusCode status, string code, string name, object value)
    {
        var extensions = new Dictionary<string, object> { [name] = value };
        return IdentityStubs.Problem(status, code, extensions);
    }

    /// <summary>The refund on a cancelled booking, as Booking's BookingRefund.</summary>
    public static object RefundOf(string reason, int amount, string status)
    {
        return new { reason, amount, status, requestedAt = "2026-10-01T06:00:00Z", completedAt = (string?)null };
    }

    /// <summary>The cancellation on a cancelled booking, as Booking's BookingCancellation.</summary>
    public static object CancellationOf(string kind, string? reason, int percent, int amount)
    {
        return new { kind, reason, refundPercent = percent, refundAmount = amount, cancelledAt = "2026-10-01T06:00:00Z" };
    }

    /// <summary>One line of Booking's customer list (a BookingSummary): the example trip with the seats and status given.</summary>
    public static object ListItem(Guid bookingId, string status, string[] seatNos, int total, object? refund)
    {
        return new
        {
            bookingId,
            status,
            trip = new BookingStub().Trip(),
            seatNos,
            totalAmount = total,
            currency = "INR",
            createdAt = "2026-10-01T05:00:00Z",
            refund,
        };
    }

    /// <summary>A page of Booking's customer list (a BookingPage).</summary>
    public static HttpResponseMessage ListPage(int page, int totalItems, params object[] items)
    {
        return FakeBackend.Json(new { items, page, pageSize = 20, totalItems });
    }

    /// <summary>The text of the page's alert, trimmed; empty when there is none.</summary>
    public static string AlertText(AngleSharp.Dom.IDocument page)
    {
        AngleSharp.Dom.IElement? alert = page.QuerySelector("[data-page-alert]");
        if (alert is null)
        {
            return string.Empty;
        }

        return alert.TextContent.Trim();
    }
}
