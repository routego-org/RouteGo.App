using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Passengers;

public sealed class PassengersTests
{
    [Fact]
    public async Task Get_ConfirmedPassengers_ListsThemInBookingsOrderWithoutContactDetails()
    {
        // Arrange
        using FleetTestHost host = Host(PassengerStubs.List);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(PassengerStubs.PageAddressWithRoute);

        // Assert
        Assert.Equal("Passengers: Mumbai → Pune, 2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.StartsWith("Passengers: Mumbai → Pune, 2 Oct 2026, 9:00 PM IST", page.Title);
        Assert.Equal("3 seats booked.", page.QuerySelector("[data-seats-booked]")!.TextContent);
        Assert.Equal(new[] { "1C", "3A", "3B" }, page.QuerySelectorAll("[data-passenger-row]").Select(row => row.GetAttribute("data-passenger-row")!).ToArray());
        Assert.Equal(new[] { "1C", "Seater", "Sameer Rao", "29", "Male", PassengerStubs.OtherBookingId.ToString() }, Cells(page, "1C"));
        Assert.Equal(new[] { "3A", "Seater", "Priya Shah", "34", "Female", BookingStubs.BookingId.ToString() }, Cells(page, "3A"));
        Assert.Equal(new[] { "3B", "Seater", "Ravi Shah", "36", "Male", BookingStubs.BookingId.ToString() }, Cells(page, "3B"));
        Assert.DoesNotContain("@", page.QuerySelector("main")!.TextContent);
        Assert.Equal("/operator/trips", page.QuerySelector("[data-back-to-trips]")!.GetAttribute("href"));
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_OneSeat_SaysSeatNotSeats()
    {
        // Arrange
        object only = PassengerStubs.Passenger("2C", PassengerStubs.OtherBookingId, "Sameer Rao", 29, "male");
        using FleetTestHost host = Host(() => PassengerStubs.Answer(1, only));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(PassengerStubs.PageAddress);

        // Assert
        Assert.Equal("1 seat booked.", page.QuerySelector("[data-seats-booked]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-passenger-row='1C']"));
        Assert.NotNull(page.QuerySelector("[data-passenger-row='2C']"));
    }

    [Fact]
    public async Task Get_NoBookings_SaysSoWithTheSameHeadingAndBackLink()
    {
        // Arrange
        using FleetTestHost host = Host(() => PassengerStubs.Answer(0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(PassengerStubs.PageAddressWithRoute);

        // Assert
        Assert.Equal("0 seats booked.", page.QuerySelector("[data-seats-booked]")!.TextContent);
        Assert.Equal("No seats are booked on this trip yet.", page.QuerySelector("[data-no-passengers]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-passengers]"));
        Assert.StartsWith("Passengers: Mumbai → Pune", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.NotNull(page.QuerySelector("[data-back-to-trips]"));
    }

    [Fact]
    public async Task Get_WithoutRoute_HeadsThePageWithTheDepartureAlone()
    {
        // Arrange
        using FleetTestHost host = Host(PassengerStubs.List);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(PassengerStubs.PageAddress);

        // Assert
        Assert.Equal("Passengers: trip leaving 2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
    }

    [Fact]
    public async Task Get_AnotherCompanysTrip_ShowsForbiddenWithBackLinkAndNoPassengers()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(PassengerStubs.PageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Equal("/operator/trips", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-passengers]"));
        Assert.Null(page.QuerySelector("[data-heading]"));
    }

    [Fact]
    public async Task Get_TripBookingDoesNotKnowYet_SaysNotAvailableYetWithTryAgainAndBack()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(PassengerStubs.PageAddressWithRoute);

        // Assert
        Assert.StartsWith(PassengersModel.NotAvailableYet, BookingStubs.AlertText(page));
        IElement[] links = page.QuerySelectorAll("[data-page-alert] a").ToArray();
        Assert.Equal("Try again", links[0].TextContent);
        Assert.Equal(PassengerStubs.PageAddressWithRoute, links[0].GetAttribute("href"));
        Assert.Equal("Back to trips", links[1].TextContent);
        Assert.Null(page.QuerySelector("[data-heading]"));
    }

    [Fact]
    public async Task Get_AddressThatIsNoIdentifier_SaysNotAvailableYetWithoutCallingBooking()
    {
        // Arrange
        using FleetTestHost host = Host(PassengerStubs.List);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync("/operator/trips/T-101/passengers");

        // Assert
        Assert.StartsWith(PassengersModel.NotAvailableYet, BookingStubs.AlertText(page));
        Assert.Empty(host.Booking.Requests);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public async Task Get_CustomerOrAdmin_ShowsNoPermissionWithoutCallingBooking(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(PassengerStubs.PageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(PassengerStubs.PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={PassengerStubs.PageAddress}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_BookingDown_ShowsUpstreamMessageAndKeepsTheMenu()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(PassengerStubs.PageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, PassengerStubs.Path, answer);
        return host;
    }

    private static string[] Cells(IDocument page, string seatNo)
    {
        IElement row = page.QuerySelector($"[data-passenger-row='{seatNo}']")!;
        return row.QuerySelectorAll("td").Select(cell => cell.TextContent).ToArray();
    }
}
