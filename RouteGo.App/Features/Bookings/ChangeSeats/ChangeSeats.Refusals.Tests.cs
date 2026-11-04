using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.ChangeSeats;

public sealed partial class ChangeSeatsTests
{
    [Fact]
    public async Task Get_AllChangesUsed_SaysSoWithoutCallingSeatInventory()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { SeatChangeCount = 3 }.Answer, () => SeatMap("5C"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith("You have used all 3 seat changes for this booking.", BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-change-form]"));
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Fact]
    public async Task Change_LimitReachedFromAnOlderPage_SaysNothingWasChanged()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => BookingStubs.Refusal(HttpStatusCode.UnprocessableEntity, "SEAT_CHANGE_LIMIT_REACHED", "maxSeatChanges", 3));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("You have used all 3 seat changes for this booking. Nothing was changed.", BookingStubs.AlertText(after));
    }

    [Fact]
    public async Task Get_LessThanThirtyMinutesBeforeDeparture_ShowsBookingClosedAndNoChoices()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { DepartureAt = "2026-10-02T10:20:00Z" }.Answer, () => SeatMap("5C"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith("This can't be done any more. The trip leaves at 2 Oct 2026, 3:50 PM IST, and booking and seat changes close 30 minutes before. Nothing was changed.", BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-change-form]"));
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Fact]
    public async Task Change_BookingClosedMeanwhile_ShowsBookingClosedWithTheDepartureBookingGives()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C"));
        var extensions = new Dictionary<string, object> { ["departureAt"] = "2026-10-02T15:30:00Z", ["cutoffMinutes"] = 30 };
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => BookingStubs.Refusal(HttpStatusCode.Conflict, "BOOKING_CLOSED", extensions));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("This can't be done any more. The trip leaves at 2 Oct 2026, 9:00 PM IST, and booking and seat changes close 30 minutes before.", BookingStubs.AlertText(after));
    }

    [Fact]
    public async Task Get_TripHasLeft_ShowsTripDepartedAndNoChoices()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { DepartureAt = "2026-10-02T09:30:00Z" }.Answer, () => SeatMap("5C"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemTripDeparted, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-change-form]"));
    }

    [Fact]
    public async Task Change_TripCancelledMeanwhile_ShowsTripCancelled()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => BookingStubs.Refusal(HttpStatusCode.Conflict, "TRIP_CANCELLED"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemTripCancelled, BookingStubs.AlertText(after));
    }

    [Theory]
    [InlineData("paymentPending", "Waiting for payment")]
    [InlineData("cancelled", "Cancelled")]
    public async Task Get_BookingNotConfirmed_NamesItsStatusWithoutCallingSeatInventory(string status, string word)
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { Status = status }.Answer, () => SeatMap("5C"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith($"This booking is {word}, so this can't be done. Nothing was changed.", BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-change-form]"));
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Fact]
    public async Task Get_SeatInventoryDoesNotKnowTheTrip_ShowsSeatDetailsUnavailableWithTryAgainAndViewBooking()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, BookingStubs.AlertText(page));
        IElement[] links = page.QuerySelectorAll("[data-page-alert] a").ToArray();
        Assert.Equal("Try again", links[0].TextContent);
        Assert.Equal(_pageAddress, links[0].GetAttribute("href"));
        Assert.Equal("View booking", links[1].TextContent);
        Assert.Null(page.QuerySelector("[data-change-form]"));
    }

    [Fact]
    public async Task Get_SeatInventoryOrBookingDown_ShowsUpstreamMessageAndNoChoices()
    {
        // Arrange
        using FleetTestHost seatsDown = Host(new BookingStub().Answer, Down);
        using FleetTestHost bookingDown = Host(Down, () => SeatMap("5C"));
        FormClient first = await seatsDown.SignInAsync(PortalSession.RoleCustomer);
        FormClient second = await bookingDown.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument seatsPage = await first.GetPageAsync(_pageAddress);
        IDocument bookingPage = await second.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(seatsPage));
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(bookingPage));
        Assert.Null(seatsPage.QuerySelector("[data-change-form]"));
        Assert.Null(bookingPage.QuerySelector("[data-change-form]"));
    }

    [Fact]
    public async Task Change_WhenBookingCannotBeReached_ShowsUpstreamMessageAndSendsTheChangeOnce()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, Down);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(after));
        Assert.Equal(1, host.Booking.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Get_AnotherCustomersBooking_ShowsForbiddenWithMyBookingsAndNoBooking()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"), () => SeatMap("5C"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Equal("/my-bookings", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Fact]
    public async Task Get_UnknownBookingOrNotAnIdentifier_ShowsNotFoundAndCallsBookingOnlyForTheFirst()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"), () => SeatMap("5C"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument unknown = await browser.GetPageAsync(_pageAddress);
        int calls = host.Booking.Requests.Count;
        IDocument notAnId = await browser.GetPageAsync("/my-bookings/B-404/change-seats");

        // Assert
        Assert.StartsWith("We couldn't find that booking. It may have been removed.", BookingStubs.AlertText(unknown));
        Assert.StartsWith("We couldn't find that booking.", BookingStubs.AlertText(notAnId));
        Assert.Equal(calls, host.Booking.Requests.Count);
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
    public async Task Get_OperatorOrAdmin_ShowsNoPermissionWithoutCallingBookingOrSeatInventory(string role)
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
        Assert.Empty(host.SeatInventory.Requests);
    }
}
