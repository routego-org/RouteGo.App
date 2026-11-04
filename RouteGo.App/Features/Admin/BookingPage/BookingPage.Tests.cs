using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.BookingPage;

public sealed class BookingPageTests
{
    private static readonly string _address = $"/admin/bookings/{BookingStubs.BookingId}";

    [Fact]
    public async Task Get_ConfirmedBooking_ShowsEverythingAboutItAndTheLinks()
    {
        // Arrange
        using FleetTestHost host = Host(() => new BookingStub().Answer());
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal($"Booking {BookingStubs.BookingId}", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Confirmed", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal(BookingStubs.BookingId.ToString(), Text(page, "data-booking-id"));
        Assert.Equal("Priya Shah", Text(page, "data-contact-name"));
        Assert.Equal("priya@example.com", Text(page, "data-contact-email"));
        Assert.Equal("Mumbai → Pune", Text(page, "data-route"));
        Assert.Equal("2 Oct 2026", Text(page, "data-travel-date"));
        Assert.Equal("2 Oct 2026, 9:00 PM IST", Text(page, "data-departs"));
        Assert.Equal("Shivneri Sleeper", Text(page, "data-bus"));
        Assert.Equal("BusCo Travels", Text(page, "data-company"));
        Assert.Equal(new[] { "3A", "Seater", "₹800", "Priya Shah", "34", "Female" }, Cells(page, "3A"));
        Assert.Equal(new[] { "3B", "Seater", "₹800", "Ravi Shah", "36", "Male" }, Cells(page, "3B"));
        Assert.Equal("₹1,600", Text(page, "data-total"));
        Assert.Equal("1 Oct 2026, 10:30 AM IST", Text(page, "data-booked-at"));
        Assert.Equal("1 Oct 2026, 10:32 AM IST", Text(page, "data-confirmed-at"));
        Assert.Equal("0 of 3 used", Text(page, "data-seat-changes"));
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_ConfirmedBooking_OffersTicketHistorySeatsBackAndForceCancel()
    {
        // Arrange
        using FleetTestHost host = Host(() => new BookingStub().Answer());
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal($"/tickets/{BookingStubs.BookingId}", Href(page, "data-view-ticket"));
        Assert.Equal($"/admin/audit-log?kind=Booking&id={BookingStubs.BookingId}", Href(page, "data-see-history"));
        Assert.Equal($"/admin/trips/{BookingStubs.TripId}/seats?route=Mumbai%20%E2%86%92%20Pune", Href(page, "data-see-seats"));
        Assert.Equal("/admin/bookings", Href(page, "data-back-to-search"));
        Assert.Equal("See history", page.QuerySelector("[data-see-history]")!.TextContent);
        Assert.Equal("See the trip's seats", page.QuerySelector("[data-see-seats]")!.TextContent);
        Assert.Equal("Back to search", page.QuerySelector("[data-back-to-search]")!.TextContent);
        Assert.Equal($"{_address}?forceCancel=true", Href(page, "data-force-cancel"));
    }

    [Fact]
    public async Task Get_BookingThatFailed_ShowsWhyAndOffersNeitherTicketNorForceCancel()
    {
        // Arrange
        var booking = new BookingStub { Status = "failed", FailureReason = "paymentDeclined", PaymentFailureDetail = "Card declined by the bank", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal("Failed", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal(BookingDisplay.FailureLabel("paymentDeclined"), page.QuerySelector("[data-outcome=failure]")!.TextContent);
        Assert.Equal("The payment service said: Card declined by the bank", page.QuerySelector("[data-outcome=payment-detail]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-view-ticket]"));
        Assert.Null(page.QuerySelector("[data-force-cancel]"));
        Assert.Null(page.QuerySelector("[data-seat-changes]"));
    }

    [Fact]
    public async Task Get_BookingWaitingForPayment_ShowsHoldEndAndCountdownButNoPaymentAddressOrForceCancel()
    {
        // Arrange
        var booking = new BookingStub { Status = "paymentPending", HoldExpiresAt = "2026-10-02T10:05:00Z", CheckoutUrl = "https://pay.example/checkout/abc", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal("Waiting for payment", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("Seats held until 2 Oct 2026, 3:35 PM IST", page.QuerySelector("[data-hold-ends]")!.TextContent);
        Assert.Equal("300", page.QuerySelector("[data-hold-countdown]")!.GetAttribute("data-seconds-left"));
        Assert.Null(page.QuerySelector("[data-force-cancel]"));
        Assert.DoesNotContain("pay.example", page.Body!.InnerHtml);
    }

    [Fact]
    public async Task Get_BookingCancelledByRouteGo_ShowsWhoWhyPercentageAndRefund()
    {
        // Arrange
        var booking = new BookingStub
        {
            Status = "cancelled",
            Cancellation = BookingStubs.CancellationOf("adminCancelled", "Bus broke down", 100, 1600),
            Refund = BookingStubs.RefundOf("cancellation", 1600, "requested"),
        };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal("Cancelled by RouteGo", page.QuerySelector("[data-outcome=kind]")!.TextContent);
        Assert.Equal("Reason: Bus broke down", page.QuerySelector("[data-outcome=reason]")!.TextContent);
        Assert.Equal("Refund percentage: 100%", page.QuerySelector("[data-outcome=percent]")!.TextContent);
        Assert.Equal("Cancellation · ₹1,600 · Refund requested", page.QuerySelector("[data-outcome=refund]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-force-cancel]"));
    }

    [Fact]
    public async Task Get_OpenedFromASearch_BackToSearchKeepsItsCriteriaAndDropsAnythingElse()
    {
        // Arrange
        using FleetTestHost host = Host(() => new BookingStub().Answer());
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);
        string back = Uri.EscapeDataString("email=priya%40example.com&page=2&evil=https%3A%2F%2Fexample.org");

        // Act
        IDocument page = await browser.GetPageAsync($"{_address}?back={back}");

        // Assert
        Assert.Equal("/admin/bookings?email=priya%40example.com&page=2", Href(page, "data-back-to-search"));
    }

    [Fact]
    public async Task Get_UnknownBooking_ShowsNotFoundWithTheWayBackToSearch()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.StartsWith(Messages.ProblemNotFound("booking"), BookingStubs.AlertText(page));
        Assert.Equal("/admin/bookings", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal("Back to search", page.QuerySelector("[data-page-alert] a")!.TextContent);
    }

    [Fact]
    public async Task Get_AddressThatIsNoIdentifier_ShowsNotFoundWithoutCallingBooking()
    {
        // Arrange
        using FleetTestHost host = Host(() => new BookingStub().Answer());
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings/B-404");

        // Assert
        Assert.StartsWith(Messages.ProblemNotFound("booking"), BookingStubs.AlertText(page));
        Assert.Empty(host.Booking.Requests);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    public async Task Get_CustomerOrOperator_ShowsNoPermissionEvenForTheirOwnBookingAndDoesNotCallBooking(string role)
    {
        // Arrange
        using FleetTestHost host = Host(() => new BookingStub().Answer());
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(_address);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheBookingAsReturnAddress()
    {
        // Arrange
        using FleetTestHost host = Host(() => new BookingStub().Answer());
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(_address);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={_address}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_BookingForbids_ShowsForbidden()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal(Messages.ProblemForbidden, BookingStubs.AlertText(page));
    }

    [Fact]
    public async Task Get_BookingDown_ShowsUpstreamAndKeepsTheMenu()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    private static string Text(IDocument page, string marker)
    {
        return page.QuerySelector($"[{marker}]")!.TextContent.Trim();
    }

    private static string? Href(IDocument page, string marker)
    {
        return page.QuerySelector($"[{marker}]")!.GetAttribute("href");
    }

    private static string[] Cells(IDocument page, string seatNo)
    {
        return page.QuerySelectorAll($"[data-seat-row='{seatNo}'] td").Select(cell => cell.TextContent.Trim()).ToArray();
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingPath, answer);
        return host;
    }
}
