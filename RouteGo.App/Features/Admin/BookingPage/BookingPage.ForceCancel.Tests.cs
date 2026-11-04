using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.BookingPage;

public sealed class BookingPageForceCancelTests
{
    private const string Reason = "Bus did not arrive at the stop";
    private static readonly string _address = $"/admin/bookings/{BookingStubs.BookingId}";

    [Fact]
    public async Task Get_ForceCancel_AsksTheQuestionNamingTheBookingAndContactWithAFullRefund()
    {
        // Arrange
        using FleetTestHost host = Host(() => AdminBookingStubs.Created(100, 1600, "requested"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Assert
        Assert.Equal($"Force-cancel booking {BookingStubs.BookingId} for Priya Shah? Its seats go back on sale and the customer is refunded in full. This cannot be undone.", page.QuerySelector("[data-question]")!.TextContent);
        Assert.Equal("Force-cancel", page.QuerySelector("[data-confirm-force-cancel]")!.TextContent);
        Assert.Equal(_address, page.QuerySelector("[data-go-back]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-force-cancel]"));
        Assert.Empty(host.Booking.Requests.Where(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Post_ReasonWithNoPercentage_CancelsInFullAndShowsTheRefundAndTheCancelledBooking()
    {
        // Arrange
        bool cancelled = false;
        using FleetTestHost host = Host(() => Booking(cancelled), () =>
        {
            cancelled = true;
            return AdminBookingStubs.Created(100, 1600, "requested");
        });
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(Reason, string.Empty));
        IDocument page = await browser.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{{\"reason\":\"{Reason}\",\"refundPercent\":null}}", host.Booking.LastRequestBody);
        Assert.Equal("Booking force-cancelled. Refund: 100% — ₹1,600. Refund requested.", page.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Cancelled", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal(2, host.Booking.Requests.Count(request => request.Method == HttpMethod.Get));
    }

    [Fact]
    public async Task Post_FiftyPercent_SendsItAndShowsTheAmountBookingGives()
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => AdminBookingStubs.Created(50, 549, "requested"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields("Partial refund agreed with customer", "50"));
        IDocument page = await browser.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal("{\"reason\":\"Partial refund agreed with customer\",\"refundPercent\":50}", host.Booking.LastRequestBody);
        Assert.Equal("Booking force-cancelled. Refund: 50% — ₹549. Refund requested.", page.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task Post_ZeroPercent_SaysNoRefundIsDue()
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => AdminBookingStubs.Created(0, 0, null));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields("Duplicate booking, customer travelled", "0"));
        IDocument page = await browser.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Contains("\"refundPercent\":0", host.Booking.LastRequestBody);
        Assert.Equal("Booking force-cancelled. No refund is due.", page.QuerySelector("[data-flash]")!.TextContent);
    }

    [Theory]
    [InlineData("too short", "", "data-reason-error", "Reason must be between 10 and 500 characters.")]
    [InlineData(Reason, "101", "data-percent-error", "Refund percentage must be between 0 and 100.")]
    [InlineData(Reason, "-1", "data-percent-error", "Refund percentage must be between 0 and 100.")]
    [InlineData(Reason, "12.5", "data-percent-error", "Enter a valid refund percentage.")]
    public async Task Post_ReasonOrPercentageOutOfRange_ShowsTheMessageKeepsTheQuestionOpenAndSendsNothing(string reason, string percent, string marker, string message)
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => AdminBookingStubs.Created(100, 1600, "requested"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(reason, percent));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(message, page.QuerySelector($"[{marker}]")!.TextContent);
        Assert.Equal(reason, page.QuerySelector("#force-cancel-reason")!.TextContent);
        Assert.Equal(percent, page.QuerySelector("#force-cancel-percent")!.GetAttribute("value") ?? string.Empty);
        Assert.NotNull(page.QuerySelector("[data-force-cancel-form]"));
        Assert.Empty(host.Booking.Requests.Where(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Post_PercentageTypedButInvalidReason_TheQuestionNamesThePercentageOfTheTotal()
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => AdminBookingStubs.Created(100, 1600, "requested"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields("short", "50"));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Contains("the customer is refunded 50% of ₹1,600.", page.QuerySelector("[data-question]")!.TextContent);
    }

    [Fact]
    public async Task Post_TooOld_ShowsTheWindowMessageWithDepartureAndDaysAndLoadsTheBookingAgain()
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["departedAt"] = "2026-10-10T15:30:00Z", ["windowDays"] = 7 };
        using FleetTestHost host = Host(() => Booking(false), () => BookingStubs.Refusal(HttpStatusCode.Conflict, "FORCE_CANCEL_WINDOW_CLOSED", extensions));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(Reason, string.Empty));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("This booking is too old to force-cancel. The trip left on 10 Oct 2026, 9:00 PM IST, and a booking can be force-cancelled up to 7 days after that. Nothing was changed.", BookingStubs.AlertText(page));
        Assert.Equal(2, host.Booking.Requests.Count(request => request.Method == HttpMethod.Get));
        Assert.Null(page.QuerySelector("[data-force-cancel-form]"));
    }

    [Fact]
    public async Task Post_BookingChangedMeanwhile_ShowsWhatItIsNowAndLoadsTheBookingAgain()
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => BookingStubs.Refusal(HttpStatusCode.Conflict, "BOOKING_NOT_CONFIRMED", "bookingStatus", "cancelled"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(Reason, string.Empty));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("This booking is Cancelled, so this can't be done. Nothing was changed.", BookingStubs.AlertText(page));
        Assert.Equal(2, host.Booking.Requests.Count(request => request.Method == HttpMethod.Get));
    }

    [Fact]
    public async Task Post_AlreadyForceCancelled_SaysRouteGoAlreadyCancelledIt()
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => FakeBackend.Json(AdminBookingStubs.Cancellation(100, 1600, "requested")));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(Reason, string.Empty));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("RouteGo already cancelled this booking. Nothing was changed.", BookingStubs.AlertText(page));
    }

    [Fact]
    public async Task Post_BookingGoneMeanwhile_ShowsNotFoundWithTheWayBack()
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(Reason, string.Empty));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith(Messages.ProblemNotFound("booking"), BookingStubs.AlertText(page));
        Assert.Equal("Back to search", page.QuerySelector("[data-page-alert] a")!.TextContent);
    }

    [Fact]
    public async Task Post_BookingRefusesTheReason_ShowsTheMessageAndKeepsWhatWasTyped()
    {
        // Arrange
        using FleetTestHost host = Host(() => Booking(false), () => FakeBackend.ValidationError("reason", "Too short."));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(Reason, "25"));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ValidationReasonLength, page.QuerySelector("[data-reason-error]")!.TextContent);
        Assert.Equal(Reason, page.QuerySelector("#force-cancel-reason")!.TextContent);
        Assert.Equal("25", page.QuerySelector("#force-cancel-percent")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Post_BookingDown_ShowsUpstreamAndKeepsTheQuestionWithTheTypedReason()
    {
        // Arrange
        bool isDown = false;
        using FleetTestHost host = Host(() => Down(isDown), () => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);
        var browser = new FormClient(client);
        IDocument question = await browser.GetPageAsync($"{_address}?forceCancel=true");
        isDown = true;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-force-cancel-form]", Fields(Reason, "50"));
        IDocument page = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Equal(Reason, page.QuerySelector("#force-cancel-reason")!.TextContent);
        Assert.Contains("Priya Shah", page.QuerySelector("[data-question]")!.TextContent);
        Assert.Contains("50% of ₹1,600", page.QuerySelector("[data-question]")!.TextContent);
    }

    private static HttpResponseMessage Booking(bool cancelled)
    {
        if (cancelled)
        {
            var booking = new BookingStub
            {
                Status = "cancelled",
                Cancellation = BookingStubs.CancellationOf("adminCancelled", Reason, 100, 1600),
                Refund = BookingStubs.RefundOf("cancellation", 1600, "requested"),
            };
            return booking.Answer();
        }

        return new BookingStub().Answer();
    }

    private static HttpResponseMessage Down(bool isDown)
    {
        if (isDown)
        {
            return BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE");
        }

        return new BookingStub().Answer();
    }

    private static Dictionary<string, string> Fields(string reason, string percent)
    {
        return new Dictionary<string, string>
        {
            ["Input.Reason"] = reason,
            ["Input.RefundPercent"] = percent,
            ["Input.ContactName"] = "Priya Shah",
            ["Input.Total"] = "1600",
        };
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> booking, Func<HttpResponseMessage> forceCancel)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingPath, booking);
        host.Booking.Map(HttpMethod.Post, AdminBookingStubs.ForceCancelPath, forceCancel);
        return host;
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> forceCancel)
    {
        return Host(() => new BookingStub().Answer(), forceCancel);
    }
}
