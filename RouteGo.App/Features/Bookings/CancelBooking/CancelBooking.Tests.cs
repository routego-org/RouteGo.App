using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.CancelBooking;

public sealed class CancelBookingTests
{
    private static readonly string _pageAddress = $"{BookingStubs.PageAddress}/cancel";

    [Fact]
    public async Task Get_Confirmed_ShowsTheRefundBeforeCancelling()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(90, 989));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Cancel booking: Mumbai → Pune, 2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Cancel booking: Mumbai → Pune, 2 Oct 2026, 9:00 PM IST · RouteGo", page.Title);
        Assert.Equal("You paid ₹1,099.", page.QuerySelector("[data-paid]")!.TextContent);
        Assert.Equal("If you cancel now you get 90% back: ₹989 of ₹1,099.", page.QuerySelector("[data-refund-offer]")!.TextContent);
        Assert.Equal("This was worked out at 2 Oct 2026, 3:30 PM IST. It is worked out again when you confirm, so it can be lower if the trip is closer by then.", page.QuerySelector("[data-worked-out]")!.TextContent);
        Assert.Equal("Cancelling frees all 2 seats of this booking. This cannot be undone.", page.QuerySelector("[data-frees]")!.TextContent);
        Assert.Equal("Cancel booking", page.QuerySelector("[data-cancel]")!.TextContent);
        Assert.Equal("Keep booking", page.QuerySelector("[data-keep-booking]")!.TextContent);
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-keep-booking]")!.GetAttribute("href"));
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
        Assert.Contains(host.Booking.Requests, request => request.RequestUri!.AbsolutePath == BookingStubs.BookingPath);
        Assert.Contains(host.Booking.Requests, request => request.RequestUri!.AbsolutePath == BookingStubs.PreviewPath);
    }

    [Fact]
    public async Task Get_NoRefundDue_SaysSoAndStillOffersCancel()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(0, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("If you cancel now you get no refund.", page.QuerySelector("[data-refund-offer]")!.TextContent);
        Assert.NotNull(page.QuerySelector("[data-cancel]"));
        Assert.NotNull(page.QuerySelector("[data-keep-booking]"));
    }

    [Fact]
    public async Task Cancel_WithARefund_ShowsTheCancellationRefundAndEmailPromiseAndNoCancelAction()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(90, 989));
        host.Booking.Map(HttpMethod.Post, BookingStubs.CancelPath, () => Cancelled(90, 989, "requested"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument preview = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(preview, "form:has([data-cancel])", ShownPercent("90"));
        IDocument page = await browser.GetPageAsync(response.Headers.Location!.ToString());

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Your booking is cancelled.", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Refund: 90% — ₹989", page.QuerySelector("[data-refund]")!.TextContent);
        Assert.Equal("Refund requested", page.QuerySelector("[data-refund-status]")!.TextContent);
        Assert.Equal("We will email you when your refund is completed.", page.QuerySelector("[data-refund-email]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-lower-refund]"));
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-view-booking]")!.GetAttribute("href"));
        Assert.Equal("/my-bookings", page.QuerySelector("[data-my-bookings]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-cancel]"));
        Assert.Equal(1, host.Booking.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Cancel_WhenNoRefundIsDue_SaysNoRefundIsDue()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(0, 0));
        host.Booking.Map(HttpMethod.Post, BookingStubs.CancelPath, () => Cancelled(0, 0, null));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument preview = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(preview, "form:has([data-cancel])", ShownPercent("0"));
        IDocument page = await browser.GetPageAsync(response.Headers.Location!.ToString());

        // Assert
        Assert.Equal("Your booking is cancelled.", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("No refund is due.", page.QuerySelector("[data-no-refund]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-refund-status]"));
    }

    [Fact]
    public async Task Cancel_WhenTheTierMovedMeanwhile_SaysTheRefundIsLowerThanShown()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(90, 989));
        host.Booking.Map(HttpMethod.Post, BookingStubs.CancelPath, () => Cancelled(50, 549, "requested"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument preview = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(preview, "form:has([data-cancel])", ShownPercent("90"));
        IDocument page = await browser.GetPageAsync(response.Headers.Location!.ToString());

        // Assert
        Assert.Equal("Refund: 50% — ₹549", page.QuerySelector("[data-refund]")!.TextContent);
        Assert.Equal("The refund is 50%, not 90%, because the trip is closer now.", page.QuerySelector("[data-lower-refund]")!.TextContent);
    }

    [Fact]
    public async Task Cancel_PressedTwiceFromAnOpenPage_ShowsTheSameSuccessBothTimes()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(90, 989));
        host.Booking.Map(HttpMethod.Post, BookingStubs.CancelPath, () => Cancelled(90, 989, "requested"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument preview = await browser.GetPageAsync(_pageAddress);
        Dictionary<string, string> fields = ShownPercent("90");

        // Act
        HttpResponseMessage first = await browser.SendFormAsync(preview, "form:has([data-cancel])", fields);
        HttpResponseMessage second = await browser.SendFormAsync(preview, "form:has([data-cancel])", fields);
        IDocument page = await browser.GetPageAsync(second.Headers.Location!.ToString());

        // Assert
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal("Refund: 90% — ₹989", page.QuerySelector("[data-refund]")!.TextContent);
        Assert.Equal(string.Empty, BookingStubs.AlertText(page));
    }

    [Fact]
    public async Task Get_SuccessAddressOpenedAgain_FallsBackToTheNormalLoad()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(90, 989));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{_pageAddress}?cancelled=true");

        // Assert
        Assert.Null(page.QuerySelector("[data-refund]"));
        Assert.NotNull(page.QuerySelector("[data-cancel]"));
    }

    [Fact]
    public async Task Get_AlreadyCancelled_ShowsNotConfirmedWithViewBookingAndNoCancelAction()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Conflict, "BOOKING_NOT_CONFIRMED", "bookingStatus", "cancelled"), new BookingStub { Status = "cancelled" });
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("This booking is Cancelled, so this can't be done. Nothing was changed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim().Split("View booking")[0].Trim());
        Assert.Equal("View booking", page.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-cancel]"));
    }

    [Theory]
    [InlineData("paymentPending", "Waiting for payment")]
    [InlineData("expired", "Expired")]
    public async Task Get_BookingNotConfirmed_NamesItsStatusAndOffersNoCancelAction(string status, string word)
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Conflict, "BOOKING_NOT_CONFIRMED", "bookingStatus", status), new BookingStub { Status = status });
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith($"This booking is {word}, so this can't be done.", BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-cancel]"));
    }

    [Fact]
    public async Task Get_TripHasLeft_ShowsTripDepartedWithViewBookingAndNoCancelAction()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Conflict, "TRIP_DEPARTED", "departedAt", "2026-10-02T09:00:00Z"), new BookingStub());
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemTripDeparted, BookingStubs.AlertText(page));
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-cancel]"));
    }

    [Fact]
    public async Task Cancel_WhenTheTripLeftMeanwhile_ShowsTripDepartedAndLoadsThePageAgain()
    {
        // Arrange
        bool hasLeft = false;
        using FleetTestHost host = Host(() => PreviewOrDeparted(hasLeft), new BookingStub());
        host.Booking.Map(HttpMethod.Post, BookingStubs.CancelPath, () => BookingStubs.Refusal(HttpStatusCode.Conflict, "TRIP_DEPARTED", "departedAt", "2026-10-02T09:00:00Z"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument preview = await browser.GetPageAsync(_pageAddress);
        hasLeft = true;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(preview, "form:has([data-cancel])", ShownPercent("90"));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith(Messages.ProblemTripDeparted, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-cancel]"));
    }

    [Fact]
    public async Task Get_AnotherCustomersBooking_ShowsForbiddenWithMyBookingsAndNoBooking()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"), new BookingStub(), () => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Equal("/my-bookings", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-preview]"));
    }

    [Fact]
    public async Task Get_UnknownBookingOrNotAnIdentifier_ShowsNotFoundAndCallsBookingOnlyForTheFirst()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"), new BookingStub(), () => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument unknown = await browser.GetPageAsync(_pageAddress);
        int calls = host.Booking.Requests.Count;
        IDocument notAnId = await browser.GetPageAsync("/my-bookings/B-404/cancel");

        // Assert
        Assert.StartsWith("We couldn't find that booking. It may have been removed.", BookingStubs.AlertText(unknown));
        Assert.StartsWith("We couldn't find that booking.", BookingStubs.AlertText(notAnId));
        Assert.Equal(calls, host.Booking.Requests.Count);
    }

    [Fact]
    public async Task Get_BookingDown_ShowsUpstreamMessage()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"), new BookingStub(), () => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-cancel]"));
    }

    [Fact]
    public async Task Cancel_WhenBookingIsDown_ShowsUpstreamMessageWithViewBookingAndSendsTheCancelOnce()
    {
        // Arrange
        using FleetTestHost host = Host(Preview(90, 989));
        host.Booking.Map(HttpMethod.Post, BookingStubs.CancelPath, () => BookingStubs.Refusal(HttpStatusCode.GatewayTimeout, "UPSTREAM_TIMEOUT"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument preview = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(preview, "form:has([data-cancel])", ShownPercent("90"));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal(1, host.Booking.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(_pageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={_pageAddress}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
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
        HttpResponseMessage response = await client.GetAsync(_pageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    private static Dictionary<string, string> ShownPercent(string percent)
    {
        return new Dictionary<string, string> { ["PreviewPercent"] = percent };
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> preview)
    {
        return Host(preview, new BookingStub());
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> preview, BookingStub booking)
    {
        return Host(preview, booking, booking.Answer);
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> preview, BookingStub booking, Func<HttpResponseMessage> bookingAnswer)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingPath, bookingAnswer);
        host.Booking.Map(HttpMethod.Get, BookingStubs.PreviewPath, preview);
        return host;
    }

    private static Func<HttpResponseMessage> Preview(int percent, int amount)
    {
        return () => FakeBackend.Json(new
        {
            bookingId = BookingStubs.BookingId,
            totalAmount = 1099,
            currency = "INR",
            refundPercent = percent,
            refundAmount = amount,
            departureAt = "2026-10-03T11:30:00Z",
            calculatedAt = "2026-10-02T10:00:00Z",
        });
    }

    private static HttpResponseMessage PreviewOrDeparted(bool hasLeft)
    {
        if (hasLeft)
        {
            return BookingStubs.Refusal(HttpStatusCode.Conflict, "TRIP_DEPARTED", "departedAt", "2026-10-02T09:00:00Z");
        }

        return Preview(90, 989)();
    }

    private static HttpResponseMessage Cancelled(int percent, int amount, string? refundStatus)
    {
        return FakeBackend.Json(new
        {
            bookingId = BookingStubs.BookingId,
            kind = "customerCancelled",
            reason = (string?)null,
            refundPercent = percent,
            refundAmount = amount,
            currency = "INR",
            cancelledAt = "2026-10-02T10:00:00Z",
            refundStatus,
        });
    }
}
