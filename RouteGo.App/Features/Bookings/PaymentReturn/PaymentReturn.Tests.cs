using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.PaymentReturn;

public sealed class PaymentReturnTests
{
    private static readonly long _nowSeconds = FixedClock.Now.ToUnixTimeSeconds();

    [Fact]
    public async Task Get_Waiting_ShowsConfirmingCountdownAndRefreshesEveryTwoSeconds()
    {
        // Arrange
        var booking = new BookingStub { Status = "paymentPending", HoldExpiresAt = "2026-10-02T10:04:00Z", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.Equal("Confirming your payment", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("This usually takes a few seconds. Please do not close this page.", page.QuerySelector("[data-wait-text]")!.TextContent);
        Assert.Equal("Your seats are held for 4 minutes 0 seconds.", page.QuerySelector("[data-hold-countdown]")!.TextContent);
        Assert.Equal("240", page.QuerySelector("[data-hold-countdown]")!.GetAttribute("data-seconds-left"));
        Assert.Equal($"2;url={BookingStubs.PaymentReturnAddress}?since={_nowSeconds}", page.QuerySelector("[data-refresh]")!.GetAttribute("content"));
        Assert.Equal("Confirming your payment · RouteGo", page.Title);
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
        Assert.Equal(BookingStubs.BookingPath, host.Booking.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Get_WaitingSinceTheFirstCheck_KeepsTheStartTimeInTheNextRefresh()
    {
        // Arrange
        long started = _nowSeconds - 20;
        using FleetTestHost host = Host(new BookingStub { Status = "paymentPending" }.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{BookingStubs.PaymentReturnAddress}?since={started}");

        // Assert
        Assert.Equal($"2;url={BookingStubs.PaymentReturnAddress}?since={started}", page.QuerySelector("[data-refresh]")!.GetAttribute("content"));
        Assert.NotNull(page.QuerySelector("[data-wait-text]"));
    }

    [Fact]
    public async Task Get_WaitingWithAStartTimeInTheFuture_StartsANewMinute()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { Status = "paymentPending" }.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{BookingStubs.PaymentReturnAddress}?since={_nowSeconds + 3600}");

        // Assert
        Assert.Equal($"2;url={BookingStubs.PaymentReturnAddress}?since={_nowSeconds}", page.QuerySelector("[data-refresh]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task Get_HoldEndedWhileWaiting_ShowsNoCountdownAndKeepsWaiting()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { Status = "paymentPending", HoldExpiresAt = "2026-10-02T09:59:00Z" }.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.Null(page.QuerySelector("[data-hold-countdown]"));
        Assert.NotNull(page.QuerySelector("[data-wait-text]"));
        Assert.NotNull(page.QuerySelector("[data-refresh]"));
    }

    [Fact]
    public async Task Get_StillWaitingAfterAMinute_ShowsEmailPromiseAndStopsChecking()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { Status = "paymentPending", HoldExpiresAt = "2026-10-02T10:04:00Z" }.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{BookingStubs.PaymentReturnAddress}?since={_nowSeconds - 60}");

        // Assert
        Assert.Equal("We are still confirming your payment. This can take a few minutes. We will email you when it is done.", page.QuerySelector("[data-still-confirming]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-refresh]"));
        Assert.Null(page.QuerySelector("[data-hold-countdown]"));
        Assert.Equal(new[] { "View booking", "Try again", "My bookings" }, page.QuerySelectorAll("main a:not(.skip-link)").Where(link => link.HasAttribute("data-view-booking") || link.HasAttribute("data-try-again") || link.HasAttribute("data-my-bookings")).Select(link => link.TextContent).ToArray());
        Assert.Equal(BookingStubs.PaymentReturnAddress, page.QuerySelector("[data-try-again]")!.GetAttribute("href"));
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-view-booking]")!.GetAttribute("href"));
        Assert.Equal("/my-bookings", page.QuerySelector("[data-my-bookings]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_Confirmed_ShowsTripSeatsTotalAndLinksAndDoesNotCheckAgain()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.Equal("Your booking is confirmed.", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Mumbai → Pune", page.QuerySelector("[data-route]")!.TextContent);
        Assert.Equal("2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-departure]")!.TextContent);
        Assert.Equal("3A, 3B", page.QuerySelector("[data-seats]")!.TextContent);
        Assert.Equal("Total ₹1,600", page.QuerySelector("[data-total]")!.TextContent);
        Assert.Equal("View ticket", page.QuerySelector("[data-view-ticket]")!.TextContent);
        Assert.Equal($"/tickets/{BookingStubs.BookingId}", page.QuerySelector("[data-view-ticket]")!.GetAttribute("href"));
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-view-booking]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-wait-text]"));
        Assert.Null(page.QuerySelector("[data-refresh]"));
        Assert.Equal("Booking confirmed · RouteGo", page.Title);
    }

    [Fact]
    public async Task Get_Declined_ShowsReasonPaymentDetailAndSearchLink()
    {
        // Arrange
        var booking = new BookingStub { Status = "failed", FailureReason = "paymentDeclined", PaymentFailureDetail = "Insufficient funds", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.Equal("Your booking was not completed.", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("The payment did not go through.", page.QuerySelector("[data-outcome=failure]")!.TextContent);
        Assert.Equal("The payment service said: Insufficient funds", page.QuerySelector("[data-outcome=payment-detail]")!.TextContent);
        Assert.Equal("Search for buses", page.QuerySelector("[data-search-buses]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-refresh]"));
    }

    [Fact]
    public async Task Get_FailedWithoutPaymentDetail_ShowsNoPaymentServiceLine()
    {
        // Arrange
        var booking = new BookingStub { Status = "failed", FailureReason = "seatsLost", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.StartsWith("The seats were no longer held when the payment arrived.", page.QuerySelector("[data-outcome=failure]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-outcome=payment-detail]"));
    }

    [Fact]
    public async Task Get_ExpiredWithLateRefund_ShowsStatusNoteAndRefundLine()
    {
        // Arrange
        var booking = new BookingStub { Status = "expired", ConfirmedAt = null, Refund = BookingStubs.RefundOf("latePayment", 1600, "requested") };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.Equal("Expired", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal(BookingDisplay.ExpiredNote, page.QuerySelector("[data-outcome=note]")!.TextContent);
        Assert.Equal("Late payment · ₹1,600 · Refund requested", page.QuerySelector("[data-outcome=refund]")!.TextContent);
    }

    [Fact]
    public async Task Get_CancelledByCustomer_ShowsStatusKindAndRefundLine()
    {
        // Arrange
        var booking = new BookingStub
        {
            Status = "cancelled",
            Cancellation = BookingStubs.CancellationOf("customerCancelled", null, 90, 1440),
            Refund = BookingStubs.RefundOf("cancellation", 1440, "requested"),
        };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.Equal("Cancelled", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("Cancelled by the customer", page.QuerySelector("[data-outcome=kind]")!.TextContent);
        Assert.Equal("Cancellation · ₹1,440 · Refund requested", page.QuerySelector("[data-outcome=refund]")!.TextContent);
    }

    [Fact]
    public async Task Get_AnotherCustomersBooking_ShowsForbiddenAndNoBooking()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-heading]"));
        Assert.Null(page.QuerySelector("[data-refresh]"));
    }

    [Fact]
    public async Task Get_UnknownBooking_ShowsNotFoundWithMyBookingsLink()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.StartsWith("We couldn't find that booking. It may have been removed.", BookingStubs.AlertText(page));
        Assert.Equal("/my-bookings", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_AddressIsNotABookingId_ShowsNotFoundWithoutCallingBooking()
    {
        // Arrange
        using var host = new FleetTestHost();
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync("/my-bookings/not-a-booking/payment-return");

        // Assert
        Assert.StartsWith("We couldn't find that booking.", BookingStubs.AlertText(page));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_BookingDownWhileWaiting_ShowsUpstreamMessageAndChecksAgain()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{BookingStubs.PaymentReturnAddress}?since={_nowSeconds - 10}");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Equal($"2;url={BookingStubs.PaymentReturnAddress}?since={_nowSeconds - 10}", page.QuerySelector("[data-refresh]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task Get_BookingDownAfterAMinute_ShowsUpstreamMessageAndStopsChecking()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.GatewayTimeout, "UPSTREAM_TIMEOUT"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{BookingStubs.PaymentReturnAddress}?since={_nowSeconds - 61}");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-refresh]"));
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(BookingStubs.PaymentReturnAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = Uri.UnescapeDataString(response.Headers.Location!.ToString());
        Assert.Contains("/auth/login", location);
        Assert.Contains($"returnUrl={BookingStubs.PaymentReturnAddress}", location);
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
        HttpResponseMessage response = await client.GetAsync(BookingStubs.PaymentReturnAddress);
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
}
