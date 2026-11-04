using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.Booking;

public sealed class BookingTests
{
    [Fact]
    public async Task Get_Confirmed_ShowsTripSeatsMoneyAndActions()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal($"Booking {BookingStubs.BookingId}", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Confirmed", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("Mumbai → Pune", page.QuerySelector("[data-route]")!.TextContent);
        Assert.Equal("2 Oct 2026", page.QuerySelector("[data-travel-date]")!.TextContent);
        Assert.Equal("2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-departs]")!.TextContent);
        Assert.Equal("3 Oct 2026, 2:30 AM IST", page.QuerySelector("[data-arrives]")!.TextContent);
        Assert.Equal("Shivneri Sleeper", page.QuerySelector("[data-bus]")!.TextContent);
        Assert.Equal("Sleeper", page.QuerySelector("[data-bus-type]")!.TextContent);
        Assert.Equal("BusCo Travels", page.QuerySelector("[data-company]")!.TextContent);
        Assert.Equal(new[] { "3A", "Seater", "₹800", "Priya Shah", "34", "Female" }, Cells(page, "3A"));
        Assert.Equal(new[] { "3B", "Seater", "₹800", "Ravi Shah", "36", "Male" }, Cells(page, "3B"));
        Assert.Equal("₹1,600", page.QuerySelector("[data-total]")!.TextContent);
        Assert.Equal("Priya Shah", page.QuerySelector("[data-contact-name]")!.TextContent);
        Assert.Equal("priya@example.com", page.QuerySelector("[data-contact-email]")!.TextContent);
        Assert.Equal("1 Oct 2026, 10:30 AM IST", page.QuerySelector("[data-booked-at]")!.TextContent);
        Assert.Equal("1 Oct 2026, 10:32 AM IST", page.QuerySelector("[data-confirmed-at]")!.TextContent);
        Assert.Equal("0 of 3 used", page.QuerySelector("[data-seat-changes]")!.TextContent);
        Assert.Equal($"/tickets/{BookingStubs.BookingId}", page.QuerySelector("[data-view-ticket]")!.GetAttribute("href"));
        Assert.Equal($"{BookingStubs.PageAddress}/change-seats", page.QuerySelector("[data-change-seats]")!.GetAttribute("href"));
        Assert.Equal($"{BookingStubs.PageAddress}/cancel", page.QuerySelector("[data-cancel-booking]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-hold-countdown]"));
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_WaitingForPayment_ShowsCountdownPayNowAndGiveUpButNotTheAddress()
    {
        // Arrange
        var booking = new BookingStub { Status = "paymentPending", HoldExpiresAt = "2026-10-02T10:06:00Z", CheckoutUrl = "https://pay.example/checkout/abc", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("Waiting for payment", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("Your seats are held for 6 minutes 0 seconds.", page.QuerySelector("[data-hold-countdown]")!.TextContent);
        Assert.Equal("360", page.QuerySelector("[data-hold-countdown]")!.GetAttribute("data-seconds-left"));
        Assert.Equal("Pay now", page.QuerySelector("[data-pay-now]")!.TextContent);
        Assert.Equal("Give up this booking", page.QuerySelector("[data-give-up]")!.TextContent);
        Assert.DoesNotContain("pay.example", page.DocumentElement.OuterHtml);
        Assert.Null(page.QuerySelector("[data-confirmed-actions]"));
        Assert.Null(page.QuerySelector("[data-seat-changes]"));
    }

    [Fact]
    public async Task Get_WaitingWithoutPaymentAddress_ShowsNoPayNow()
    {
        // Arrange
        var booking = new BookingStub { Status = "seatsHeld", HoldExpiresAt = "2026-10-02T10:06:00Z", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.NotNull(page.QuerySelector("[data-hold-countdown]"));
        Assert.Null(page.QuerySelector("[data-pay-now]"));
        Assert.NotNull(page.QuerySelector("[data-give-up]"));
    }

    [Fact]
    public async Task Get_HoldEndedButStillUnpaid_ShowsTimeToPayEndedWithoutCountdownOrPayNow()
    {
        // Arrange
        var booking = new BookingStub { Status = "paymentPending", HoldExpiresAt = "2026-10-02T09:59:00Z", CheckoutUrl = "https://pay.example/checkout/abc", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("The time to pay has ended. If you already paid, we will confirm it or refund you.", page.QuerySelector("[data-time-to-pay-ended]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-hold-countdown]"));
        Assert.Null(page.QuerySelector("[data-pay-now]"));
    }

    [Fact]
    public async Task Get_AllSeatChangesUsed_ExplainsAndDoesNotOfferChangeSeats()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { SeatChangeCount = 3 }.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("3 of 3 used", page.QuerySelector("[data-seat-changes]")!.TextContent);
        Assert.Equal("You have used all 3 seat changes.", page.QuerySelector("[data-seat-changes-note]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-change-seats]"));
        Assert.NotNull(page.QuerySelector("[data-cancel-booking]"));
    }

    [Fact]
    public async Task Get_LastThirtyMinutes_ExplainsAndStillOffersCancel()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { DepartureAt = "2026-10-02T10:20:00Z" }.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("Seats can't be changed in the last 30 minutes before the trip leaves.", page.QuerySelector("[data-seat-changes-note]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-change-seats]"));
        Assert.NotNull(page.QuerySelector("[data-cancel-booking]"));
    }

    [Fact]
    public async Task Get_AfterDeparture_ShowsNoticeAndNeitherChangeNorCancel()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { DepartureAt = "2026-10-02T09:30:00Z" }.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("This trip has left.", page.QuerySelector("[data-notice]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-change-seats]"));
        Assert.Null(page.QuerySelector("[data-cancel-booking]"));
        Assert.Null(page.QuerySelector("[data-seat-changes-note]"));
        Assert.NotNull(page.QuerySelector("[data-view-ticket]"));
    }

    [Fact]
    public async Task Get_CancelledByCustomer_ShowsKindTimePercentRefundAndNoActions()
    {
        // Arrange
        var booking = new BookingStub
        {
            Status = "cancelled",
            Cancellation = BookingStubs.CancellationOf("customerCancelled", null, 90, 989),
            Refund = BookingStubs.RefundOf("cancellation", 989, "completed"),
        };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("Cancelled", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("Cancelled by the customer", page.QuerySelector("[data-outcome=kind]")!.TextContent);
        Assert.Equal("Cancelled at: 1 Oct 2026, 11:30 AM IST", page.QuerySelector("[data-outcome=cancelled-at]")!.TextContent);
        Assert.Equal("Refund percentage: 90%", page.QuerySelector("[data-outcome=percent]")!.TextContent);
        Assert.Equal("Cancellation · ₹989 · Refund completed", page.QuerySelector("[data-outcome=refund]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-confirmed-actions]"));
        Assert.Null(page.QuerySelector("[data-view-ticket]"));
        Assert.Equal("Search for buses", page.QuerySelector("[data-search-buses]")!.TextContent);
    }

    [Fact]
    public async Task Get_CancelledByAdmin_ShowsTheAdminsReason()
    {
        // Arrange
        var booking = new BookingStub
        {
            Status = "cancelled",
            Cancellation = BookingStubs.CancellationOf("adminCancelled", "Bus did not arrive at the stop", 100, 1600),
            Refund = BookingStubs.RefundOf("cancellation", 1600, "requested"),
        };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("Cancelled by RouteGo", page.QuerySelector("[data-outcome=kind]")!.TextContent);
        Assert.Equal("Reason: Bus did not arrive at the stop", page.QuerySelector("[data-outcome=reason]")!.TextContent);
    }

    [Fact]
    public async Task Get_CancelledWithNoRefundDue_SaysSo()
    {
        // Arrange
        var booking = new BookingStub { Status = "cancelled", Cancellation = BookingStubs.CancellationOf("customerCancelled", null, 0, 0) };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("No refund is due.", page.QuerySelector("[data-outcome=no-refund]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-outcome=refund]"));
    }

    [Fact]
    public async Task Get_FailedAndExpired_ShowReasonOrNoteAndOnlyTheSearchLink()
    {
        // Arrange
        var failed = new BookingStub { Status = "failed", FailureReason = "paymentDeclined", PaymentFailureDetail = "Insufficient funds", ConfirmedAt = null };
        using FleetTestHost failedHost = Host(failed.Answer);
        FormClient failedBrowser = await failedHost.SignInAsync(PortalSession.RoleCustomer);
        var expired = new BookingStub { Status = "expired", ConfirmedAt = null, Refund = BookingStubs.RefundOf("latePayment", 900, "requested") };
        using FleetTestHost expiredHost = Host(expired.Answer);
        FormClient expiredBrowser = await expiredHost.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument failedPage = await failedBrowser.GetPageAsync(BookingStubs.PageAddress);
        IDocument expiredPage = await expiredBrowser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal("Failed", failedPage.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("The payment did not go through.", failedPage.QuerySelector("[data-outcome=failure]")!.TextContent);
        Assert.Equal("The payment service said: Insufficient funds", failedPage.QuerySelector("[data-outcome=payment-detail]")!.TextContent);
        Assert.Equal("Expired", expiredPage.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal(BookingDisplay.ExpiredNote, expiredPage.QuerySelector("[data-outcome=note]")!.TextContent);
        Assert.Equal("Late payment · ₹900 · Refund requested", expiredPage.QuerySelector("[data-outcome=refund]")!.TextContent);
        Assert.Null(failedPage.QuerySelector("[data-view-ticket]"));
        Assert.Null(expiredPage.QuerySelector("[data-confirmed-actions]"));
        Assert.NotNull(failedPage.QuerySelector("[data-search-buses]"));
    }

    [Fact]
    public async Task Get_AnotherCustomersBooking_ShowsForbiddenAndNoBooking()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-heading]"));
    }

    [Fact]
    public async Task Get_UnknownBookingOrNotAnIdentifier_ShowsNotFoundAndOnlyCallsBookingForTheFirst()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument unknown = await browser.GetPageAsync(BookingStubs.PageAddress);
        int callsAfterUnknown = host.Booking.Requests.Count;
        IDocument notAnId = await browser.GetPageAsync("/my-bookings/B-404");

        // Assert
        Assert.StartsWith("We couldn't find that booking. It may have been removed.", BookingStubs.AlertText(unknown));
        Assert.Equal("/my-bookings", unknown.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.StartsWith("We couldn't find that booking.", BookingStubs.AlertText(notAnId));
        Assert.Equal(callsAfterUnknown, host.Booking.Requests.Count);
    }

    [Fact]
    public async Task Get_BookingDown_ShowsUpstreamMessage()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={BookingStubs.PageAddress}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Booking.Requests);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_OperatorOrAdmin_ShowsNoPermissionWithoutCallingBooking(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(BookingStubs.PageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingPath, answer);
        return host;
    }

    private static string[] Cells(IDocument page, string seatNo)
    {
        IElement row = page.QuerySelector($"[data-seat-row='{seatNo}']")!;
        return row.QuerySelectorAll("td").Select(cell => cell.TextContent).ToArray();
    }
}
