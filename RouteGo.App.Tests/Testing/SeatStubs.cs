using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// The operator's trip T1 the seat page tests share: BusCo Travels' Mumbai → Pune trip leaving on
/// 23 Oct 2026 at 10:30 PM IST, with L1 and L2 available, L3 booked, U1 held and U2 blocked by the
/// operator — and the addresses its page and Seat Inventory's operations live at.
/// </summary>
public static class SeatStubs
{
    public static readonly Guid TripId = Guid.Parse("5e4d3c2b-1a0f-4e9d-8c7b-6a5f4e3d2c1b");
    public static readonly Guid BookingId = Guid.Parse("9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d");

    public static readonly string AllocationsPath = $"/trips/{TripId}/seatAllocations";
    public static readonly string BlocksPath = $"/trips/{TripId}/seatBlocks";

    /// <summary>The operator's seat page of T1, reached from "Seats" on My trips.</summary>
    public static readonly string OperatorPage = $"/operator/trips/{TripId}/seats?route=Mumbai%20%E2%86%92%20Pune";

    /// <summary>T1's seats as Seat Inventory gives them to the trip's operator.</summary>
    public static object Allocations(bool isCancelled, bool isDeparted)
    {
        return new
        {
            tripId = TripId,
            operatorId = TripStubs.OperatorId,
            departureAt = "2026-10-23T17:00:00Z",
            isCancelled,
            isDeparted,
            items = new object[]
            {
                new { seatNo = "L1", deck = "lower", row = 1, column = 1, seatClass = "Lower berth", fare = 1200, status = "available", bookingId = (string?)null, holdExpiresAt = (string?)null, blockReason = (string?)null },
                new { seatNo = "L2", deck = "lower", row = 1, column = 2, seatClass = "Lower berth", fare = 1200, status = "available", bookingId = (string?)null, holdExpiresAt = (string?)null, blockReason = (string?)null },
                new { seatNo = "L3", deck = "lower", row = 2, column = 1, seatClass = "Lower berth", fare = 1200, status = "booked", bookingId = (string?)BookingId.ToString(), holdExpiresAt = (string?)null, blockReason = (string?)null },
                new { seatNo = "U1", deck = "upper", row = 1, column = 1, seatClass = "Upper berth", fare = 1000, status = "held", bookingId = (string?)BookingId.ToString(), holdExpiresAt = (string?)"2026-10-20T05:40:00Z", blockReason = (string?)null },
                new { seatNo = "U2", deck = "upper", row = 1, column = 2, seatClass = "Upper berth", fare = 1000, status = "blocked", bookingId = (string?)null, holdExpiresAt = (string?)null, blockReason = (string?)"operator" },
            },
        };
    }

    /// <summary>Booking A holds 3A and 3B since a hold that ended 3 hours before the test clock: a stuck hold.</summary>
    public static readonly Guid StuckBookingId = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");

    /// <summary>Booking B holds 5C until 6 minutes after the test clock: still running.</summary>
    public static readonly Guid RunningBookingId = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002");

    /// <summary>Booking C's hold on 6D ended 1 minute before the test clock: not stuck yet.</summary>
    public static readonly Guid JustEndedBookingId = Guid.Parse("c3c3c3c3-0000-4000-8000-000000000003");

    /// <summary>Booking D has paid for 4C.</summary>
    public static readonly Guid PaidBookingId = Guid.Parse("d4d4d4d4-0000-4000-8000-000000000004");

    /// <summary>The admin's seat page of T1, reached from "Seats" in trip search.</summary>
    public static readonly string AdminPage = $"/admin/trips/{TripId}/seats?route=Mumbai%20%E2%86%92%20Pune";

    /// <summary>Where the admin cancels booking A's hold.</summary>
    public static readonly string CancelHoldPath = $"/trips/{TripId}/holds/{StuckBookingId}/cancel";

    /// <summary>T1's seats as Seat Inventory gives them to an admin, at the test clock (2 Oct 2026, 10:00 UTC).</summary>
    public static object StaffAllocations(bool isCancelled, bool isDeparted)
    {
        return new
        {
            tripId = TripId,
            operatorId = TripStubs.OperatorId,
            departureAt = "2026-10-23T17:00:00Z",
            isCancelled,
            isDeparted,
            items = new object[]
            {
                new { seatNo = "3A", deck = "lower", row = 1, column = 1, seatClass = "Seater", fare = 600, status = "held", bookingId = (string?)StuckBookingId.ToString(), holdExpiresAt = (string?)"2026-10-02T07:00:00Z", blockReason = (string?)null },
                new { seatNo = "3B", deck = "lower", row = 1, column = 2, seatClass = "Seater", fare = 600, status = "held", bookingId = (string?)StuckBookingId.ToString(), holdExpiresAt = (string?)"2026-10-02T07:00:00Z", blockReason = (string?)null },
                new { seatNo = "5C", deck = "lower", row = 2, column = 1, seatClass = "Seater", fare = 600, status = "held", bookingId = (string?)RunningBookingId.ToString(), holdExpiresAt = (string?)"2026-10-02T10:06:00Z", blockReason = (string?)null },
                new { seatNo = "6D", deck = "lower", row = 2, column = 2, seatClass = "Seater", fare = 600, status = "held", bookingId = (string?)JustEndedBookingId.ToString(), holdExpiresAt = (string?)"2026-10-02T09:59:00Z", blockReason = (string?)null },
                new { seatNo = "4C", deck = "lower", row = 3, column = 1, seatClass = "Seater", fare = 600, status = "booked", bookingId = (string?)PaidBookingId.ToString(), holdExpiresAt = (string?)null, blockReason = (string?)null },
                new { seatNo = "L1", deck = "lower", row = 3, column = 2, seatClass = "Seater", fare = 600, status = "blocked", bookingId = (string?)null, holdExpiresAt = (string?)null, blockReason = (string?)"operator" },
                new { seatNo = "L2", deck = "lower", row = 4, column = 1, seatClass = "Seater", fare = 600, status = "available", bookingId = (string?)null, holdExpiresAt = (string?)null, blockReason = (string?)null },
            },
        };
    }

    /// <summary>A refusal naming seats in one of its extension members.</summary>
    public static HttpResponseMessage SeatsProblem(HttpStatusCode status, string code, string extension, params string[] seatNos)
    {
        var extensions = new Dictionary<string, object> { [extension] = seatNos };
        return IdentityStubs.Problem(status, code, extensions);
    }

    /// <summary>The seat numbers in a DELETE seatBlocks request's query, as Seat Inventory reads them.</summary>
    public static string UnblockedSeats(FakeBackend seatInventory)
    {
        HttpRequestMessage request = seatInventory.Requests.Last(sent => sent.Method == HttpMethod.Delete);
        return Uri.UnescapeDataString(request.RequestUri!.Query);
    }

    /// <summary>The seat numbers that can be ticked on the drawn page, in page order.</summary>
    public static string[] Pickable(IDocument page)
    {
        return page.QuerySelectorAll("[data-pick]").Select(box => box.GetAttribute("value")!).ToArray();
    }

    /// <summary>The seat numbers ticked on the drawn page, in page order.</summary>
    public static string[] Ticked(IDocument page)
    {
        return page.QuerySelectorAll("[data-pick][checked]").Select(box => box.GetAttribute("value")!).ToArray();
    }
}
