using AngleSharp.Dom;

namespace RouteGo.App.Features.Seats.PublicSeatMap;

public sealed class PublicSeatMapBookTests
{
    private static readonly Guid _tripId = Guid.Parse("5e4d3c2b-1a0f-4e9d-8c7b-6a5f4e3d2c1b");
    private static readonly string _routedAddress = $"/trips/{_tripId}/seats?route=Mumbai%20%E2%86%92%20Pune";

    [Fact]
    public async Task Get_Visitor_OffersBookTheseSeatsToTheBookingPageWithTheRoute()
    {
        // Arrange
        using FleetTestHost host = Host(false, false, "available");
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_routedAddress);

        // Assert
        Assert.Equal("Book these seats", page.QuerySelector("[data-book-seats]")!.TextContent);
        Assert.Equal($"/trips/{_tripId}/book?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector("[data-book-seats]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-pick]"));
    }

    [Fact]
    public async Task Get_Customer_OffersBookTheseSeats()
    {
        // Arrange
        using FleetTestHost host = Host(false, false, "available");
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_routedAddress);

        // Assert
        Assert.NotNull(page.QuerySelector("[data-book-seats]"));
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_OperatorOrAdmin_IsNotOfferedBookTheseSeats(string role)
    {
        // Arrange
        using FleetTestHost host = Host(false, false, "available");
        FormClient browser = await host.SignInAsync(role);

        // Act
        IDocument page = await browser.GetPageAsync(_routedAddress);

        // Assert
        Assert.Null(page.QuerySelector("[data-book-seats]"));
    }

    [Theory]
    [InlineData(true, false, "blocked")]
    [InlineData(false, true, "available")]
    [InlineData(false, false, "booked")]
    [InlineData(false, false, "held")]
    [InlineData(false, false, "blocked")]
    public async Task Get_TripThatCannotBeBooked_IsNotOfferedBookTheseSeatsToAnyone(bool isCancelled, bool isDeparted, string status)
    {
        // Arrange
        using FleetTestHost host = Host(isCancelled, isDeparted, status);
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_routedAddress);

        // Assert
        Assert.Null(page.QuerySelector("[data-book-seats]"));
    }

    private static FleetTestHost Host(bool isCancelled, bool isDeparted, string status)
    {
        var host = new FleetTestHost();
        object[] seats = new object[] { new { seatNo = "L1", deck = "lower", row = 1, column = 1, seatClass = "Lower berth", fare = 1200, status } };
        host.SeatInventory.Map(HttpMethod.Get, $"/trips/{_tripId}/seats", () => FakeBackend.Json(new { tripId = _tripId, departureAt = "2026-10-23T17:00:00Z", isCancelled, isDeparted, seats }));
        return host;
    }
}
