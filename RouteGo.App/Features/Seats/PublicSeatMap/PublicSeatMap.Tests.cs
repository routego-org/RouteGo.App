using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Seats.PublicSeatMap;

public sealed class PublicSeatMapTests
{
    private static readonly Guid _tripId = Guid.Parse("5e4d3c2b-1a0f-4e9d-8c7b-6a5f4e3d2c1b");
    private static readonly string _pageAddress = $"/trips/{_tripId}/seats";
    private static readonly string _routedAddress = _pageAddress + "?route=Mumbai%20%E2%86%92%20Pune";
    private static readonly string _seatMapPath = $"/trips/{_tripId}/seats";

    [Fact]
    public async Task Get_Visitor_ShowsSeatsDeckByDeckWithoutToken()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => FakeBackend.Json(SeatMap(false, false)));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await visitor.GetAsync(_routedAddress);
        IDocument page = await new FormClient(visitor).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal(new[] { "Lower deck", "Upper deck" }, page.QuerySelectorAll("[data-deck-name]").Select(deck => deck.TextContent).ToArray());
        Assert.Equal(new[] { "L1", "L2" }, SeatNumbers(page, "lower"));
        Assert.Equal(new[] { "U1" }, SeatNumbers(page, "upper"));
        Assert.Equal("Lower berth", SeatText(page, "L1", "[data-seat-class]"));
        Assert.Equal("₹1,200", SeatText(page, "L1", "[data-fare]"));
        Assert.Equal("Available", SeatText(page, "L1", "[data-status-label]"));
        Assert.Equal("Booked", SeatText(page, "L2", "[data-status-label]"));
        Assert.Equal("Upper berth", SeatText(page, "U1", "[data-seat-class]"));
        Assert.Equal("₹1,000", SeatText(page, "U1", "[data-fare]"));
        Assert.Equal("On hold", SeatText(page, "U1", "[data-status-label]"));
        Assert.Equal("1 of 3 seats available.", page.QuerySelector("[data-available]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-pick]"));
        Assert.Null(host.SeatInventory.LastRequestHeader("Authorization"));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_SignedIn_ShowsSamePageWithoutToken(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => FakeBackend.Json(SeatMap(false, false)));
        FormClient browser = await host.SignInAsync(role);

        // Act
        IDocument page = await browser.GetPageAsync(_routedAddress);

        // Assert
        Assert.Equal("Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("1 of 3 seats available.", page.QuerySelector("[data-available]")!.TextContent);
        Assert.Empty(page.QuerySelectorAll("[data-line]"));
        Assert.Null(host.SeatInventory.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_NoRouteInAddress_HeadingNamesDepartureOnly()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => FakeBackend.Json(SeatMap(false, false)));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Seats: trip leaving 23 Oct 2026, 10:30 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
    }

    [Fact]
    public async Task Get_CancelledTrip_ShowsNoticeAndNoAvailableLine()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => FakeBackend.Json(CancelledSeatMap()));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("This trip was cancelled. Its seats will not be sold.", page.QuerySelector("[data-notice]")!.TextContent);
        Assert.Equal(new[] { "Blocked", "Blocked" }, page.QuerySelectorAll("[data-status-label]").Select(status => status.TextContent).ToArray());
        Assert.Null(page.QuerySelector("[data-available]"));
    }

    [Fact]
    public async Task Get_DepartedTrip_ShowsNoticeAndNoAvailableLine()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => FakeBackend.Json(SeatMap(false, true)));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("This trip has left.", page.QuerySelector("[data-notice]")!.TextContent);
        Assert.Equal(new[] { "Available", "Booked", "On hold" }, page.QuerySelectorAll("[data-status-label]").Select(status => status.TextContent).ToArray());
        Assert.Null(page.QuerySelector("[data-available]"));
    }

    [Fact]
    public async Task Get_UnknownTrip_ShowsUnavailableWithTryAgainAndGoHome()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await visitor.GetAsync(_routedAddress);
        IDocument page = await new FormClient(visitor).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        IElement[] links = page.QuerySelectorAll("[data-page-alert] a").ToArray();
        Assert.Equal("Try again", links[0].TextContent);
        Assert.Equal(_routedAddress, links[0].GetAttribute("href"));
        Assert.Equal("Go to Home", links[1].TextContent);
        Assert.Equal("/", links[1].GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-deck]"));
    }

    [Fact]
    public async Task Get_SeatInventoryRefusesTripIdentifier_ShowsUnavailable()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => FakeBackend.ValidationError("tripId", "tripId must be a trip identifier (a UUID)."));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
    }

    [Fact]
    public async Task Get_AddressNamesNoTrip_ShowsUnavailableWithoutCallingSeatInventory()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await visitor.GetAsync("/trips/not-a-trip/seats");
        IDocument page = await new FormClient(visitor).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Fact]
    public async Task Get_SeatInventoryUnreachable_ShowsUpstreamMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, () => throw new HttpRequestException("connection refused"));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
        Assert.Null(page.QuerySelector("[data-deck]"));
    }

    private static string[] SeatNumbers(IDocument page, string deck)
    {
        return page.QuerySelectorAll($"[data-deck='{deck}'] [data-seat]").Select(seat => seat.GetAttribute("data-seat")!).ToArray();
    }

    private static string SeatText(IDocument page, string seatNo, string selector)
    {
        return page.QuerySelector($"[data-seat='{seatNo}'] {selector}")!.TextContent;
    }

    private static object SeatMap(bool isCancelled, bool isDeparted)
    {
        return new
        {
            tripId = _tripId,
            departureAt = "2026-10-23T17:00:00Z",
            isCancelled,
            isDeparted,
            seats = new object[]
            {
                new { seatNo = "L1", deck = "lower", row = 1, column = 1, seatClass = "Lower berth", fare = 1200, status = "available" },
                new { seatNo = "L2", deck = "lower", row = 1, column = 2, seatClass = "Lower berth", fare = 1200, status = "booked" },
                new { seatNo = "U1", deck = "upper", row = 1, column = 1, seatClass = "Upper berth", fare = 1000, status = "held" },
            },
        };
    }

    private static object CancelledSeatMap()
    {
        return new
        {
            tripId = _tripId,
            departureAt = "2026-10-23T17:00:00Z",
            isCancelled = true,
            isDeparted = false,
            seats = new object[]
            {
                new { seatNo = "L1", deck = "lower", row = 1, column = 1, seatClass = "Lower berth", fare = 1200, status = "blocked" },
                new { seatNo = "L2", deck = "lower", row = 1, column = 2, seatClass = "Lower berth", fare = 1200, status = "blocked" },
            },
        };
    }
}
