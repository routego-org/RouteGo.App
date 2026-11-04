namespace RouteGo.App.Tests.Testing;

/// <summary>Booking's answers for the admin booking pages: its AdminBookingPage and Cancellation shapes.</summary>
public static class AdminBookingStubs
{
    public const string SearchPath = "/bookings/search";

    public static readonly Guid FailedBookingId = Guid.Parse("88888888-0000-4000-8000-000000000081");

    public static readonly string ForceCancelPath = $"/bookings/{BookingStubs.BookingId}/forceCancellations";

    /// <summary>The address the portal sends for a page of the search.</summary>
    public static string SearchQuery(int page)
    {
        return $"{SearchPath}?page={page}&pageSize=20";
    }

    /// <summary>Booking's AdminBookingSummary: the example trip, booked at 10:30 AM IST on 1 Oct 2026.</summary>
    public static object Item(Guid bookingId, string status, string[] seatNos, int total)
    {
        return new
        {
            bookingId,
            status,
            contactName = "Priya Shah",
            contactEmail = "priya@example.com",
            trip = new BookingStub().Trip(),
            seatNos,
            totalAmount = total,
            currency = "INR",
            createdAt = "2026-10-01T05:00:00Z",
        };
    }

    /// <summary>Booking's AdminBookingPage.</summary>
    public static HttpResponseMessage Page(int page, int totalItems, params object[] items)
    {
        return FakeBackend.Json(new { items, page, pageSize = 20, totalItems });
    }

    /// <summary>Booking's Cancellation, as a force-cancel answers it.</summary>
    public static object Cancellation(int percent, int amount, string? refundStatus)
    {
        return new
        {
            bookingId = BookingStubs.BookingId,
            kind = "adminCancelled",
            reason = "Bus did not arrive at the stop",
            refundPercent = percent,
            refundAmount = amount,
            currency = "INR",
            cancelledAt = "2026-10-02T05:30:00Z",
            refundStatus,
        };
    }

    /// <summary>The 201 a new force-cancel is answered with.</summary>
    public static HttpResponseMessage Created(int percent, int amount, string? refundStatus)
    {
        return FakeBackend.Json(Cancellation(percent, amount, refundStatus), System.Net.HttpStatusCode.Created);
    }
}
