using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.BookSeats;

public sealed class BookSeatsTests
{
    [Fact]
    public async Task Get_Customer_ShowsSeatsContactNameAndTheHeldNoteAndHoldsNothing()
    {
        // Arrange
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap());
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Assert
        Assert.Equal("Book seats: Mumbai → Pune, 2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Book seats: Mumbai → Pune, 2 Oct 2026, 9:00 PM IST · RouteGo", page.Title);
        Assert.Equal("3 of 4 seats available.", page.QuerySelector("[data-available]")!.TextContent);
        Assert.Equal("₹800", page.QuerySelector("[data-seat='3A'] [data-fare]")!.TextContent);
        Assert.Equal("Seater", page.QuerySelector("[data-seat='3A'] [data-seat-class]")!.TextContent);
        Assert.Equal("Booked", page.QuerySelector("[data-seat='4A'] [data-status-label]")!.TextContent);
        Assert.Equal(new[] { "3A", "3B", "3C" }, SeatStubs.Pickable(page));
        Assert.Equal("Priya Shah", page.QuerySelector("[data-contact-name]")!.GetAttribute("value"));
        Assert.Equal("Your seats are held for you when you choose Pay.", page.QuerySelector("[data-held-note]")!.TextContent);
        Assert.Equal("Pay", page.QuerySelector("[data-pay]")!.TextContent);
        Assert.Equal("Choose at least one seat.", page.QuerySelector("[data-choose-hint]")!.TextContent);
        Assert.Empty(BookSeatsStubs.OpenPassengers(page));
        Assert.Null(host.SeatInventory.LastRequestHeader("Authorization"));
        Assert.Equal("Bearer " + host.AccessToken, host.Identity.LastRequestHeader("Authorization"));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_ProfileCannotBeLoaded_LeavesTheContactNameEmptyAndSaysNothing()
    {
        // Arrange
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap(), () => IdentityStubs.Problem(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Assert
        Assert.Equal(string.Empty, page.QuerySelector("[data-contact-name]")!.GetAttribute("value"));
        Assert.Equal(string.Empty, BookingStubs.AlertText(page));
        Assert.NotNull(page.QuerySelector("[data-book-form]"));
    }

    [Fact]
    public async Task Get_NoRouteInAddress_HeadingNamesTheDepartureOnly()
    {
        // Arrange
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap());
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"/trips/{BookingStubs.TripId}/book");

        // Assert
        Assert.Equal("Book seats: trip leaving 2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
    }

    [Theory]
    [InlineData(true, false, "This trip was cancelled. Its seats will not be sold.")]
    [InlineData(false, true, "This trip has left.")]
    public async Task Get_CancelledOrDepartedTrip_ShowsNoticeAndNoForm(bool isCancelled, bool isDeparted, string notice)
    {
        // Arrange
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap(isCancelled, isDeparted, 800));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Assert
        Assert.Equal(notice, page.QuerySelector("[data-notice]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-book-form]"));
        Assert.Empty(SeatStubs.Pickable(page));
    }

    [Fact]
    public async Task Get_UnknownTrip_ShowsSeatDetailsUnavailableAndNoForm()
    {
        // Arrange
        using FleetTestHost host = BookSeatsStubs.Host(() => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, BookingStubs.AlertText(page));
        IElement[] links = page.QuerySelectorAll("[data-page-alert] a").ToArray();
        Assert.Equal("Try again", links[0].TextContent);
        Assert.Equal(BookSeatsStubs.RoutedAddress, links[0].GetAttribute("href"));
        Assert.Equal("Go to Home", links[1].TextContent);
        Assert.Null(page.QuerySelector("[data-book-form]"));
    }

    [Fact]
    public async Task Get_AddressNamesNoTrip_ShowsUnavailableWithoutCallingSeatInventory()
    {
        // Arrange
        using var host = new FleetTestHost();
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync("/trips/not-a-trip/book");

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, BookingStubs.AlertText(page));
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Fact]
    public async Task Get_SeatInventoryDown_ShowsUpstreamMessageAndNoForm()
    {
        // Arrange
        using FleetTestHost host = BookSeatsStubs.Host(() => IdentityStubs.Problem(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-book-form]"));
    }

    [Fact]
    public async Task Choose_TwoSeats_OpensTheirPassengerBoxesAndShowsTheTotal()
    {
        // Arrange
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap());
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        var ticks = BookSeatsStubs.Form("3A", "3B").Where(field => field.Key == "Picks" || field.Key == "ContactName").ToList();
        HttpResponseMessage response = await browser.SendButtonAsync(page, "[data-update-seats]", ticks);
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(new[] { "3A", "3B" }, BookSeatsStubs.Chosen(after));
        Assert.Equal(new[] { "3A", "3B" }, BookSeatsStubs.OpenPassengers(after));
        Assert.Equal("Total ₹1,600", after.QuerySelector("[data-total]")!.TextContent);
        Assert.Equal("Pay ₹1,600", after.QuerySelector("[data-pay]")!.TextContent);
        Assert.Null(after.QuerySelector("[data-choose-hint]"));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Choose_ASeventhSeat_IsNotChosenAndSaysThePageAllowsSix()
    {
        // Arrange
        string[] free = new[] { "1A", "1B", "1C", "1D", "2A", "2B", "2C", "2D" };
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap(free));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);
        var fields = new List<KeyValuePair<string, string>>();
        foreach (string seat in free.Take(7))
        {
            fields.Add(BookSeatsStubs.Field("Picks", seat));
        }

        // Act
        HttpResponseMessage response = await browser.SendButtonAsync(page, "[data-update-seats]", fields);
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(free.Take(6).ToArray(), BookSeatsStubs.Chosen(after));
        Assert.Equal("You can book up to 6 seats.", BookingStubs.AlertText(after));
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheBookingAddressIncludingRoute()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(BookSeatsStubs.RoutedAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl=/trips/{BookingStubs.TripId}/book?route=", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_OperatorOrAdmin_ShowsNoPermissionAndCallsNothing(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(BookSeatsStubs.RoutedAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.SeatInventory.Requests);
        Assert.Empty(host.Booking.Requests);
    }
}
