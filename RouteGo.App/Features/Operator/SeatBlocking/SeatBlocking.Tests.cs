using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.SeatBlocking;

public sealed class SeatBlockingTests
{
    [Fact]
    public async Task Get_OwnTrip_ShowsSeatsCountsAndLinksWithToken()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.OperatorPage);

        // Assert
        Assert.Equal("Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal(new[] { "Lower deck", "Upper deck" }, page.QuerySelectorAll("[data-deck-name]").Select(deck => deck.TextContent).ToArray());
        Assert.Equal("Lower berth", SeatText(page, "L1", "[data-seat-class]"));
        Assert.Equal("₹1,200", SeatText(page, "L1", "[data-fare]"));
        Assert.Equal("Available", SeatText(page, "L1", "[data-status-label]"));
        Assert.Equal("Blocked", SeatText(page, "U2", "[data-status-label]"));
        Assert.Equal("Blocked by: Operator", SeatText(page, "U2", "[data-line]"));
        Assert.Equal("Available: 2 · On hold: 1 · Booked: 1 · Blocked: 1", page.QuerySelector("[data-counts]")!.TextContent);
        Assert.Equal("See the public seat map", page.QuerySelector("[data-public-seat-map]")!.TextContent);
        Assert.Equal($"/trips/{SeatStubs.TripId}/seats?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector("[data-public-seat-map]")!.GetAttribute("href"));
        Assert.Equal("Back to my trips", page.QuerySelector("[data-back-to-trips]")!.TextContent);
        Assert.Equal("/operator/trips", page.QuerySelector("[data-back-to-trips]")!.GetAttribute("href"));
        Assert.Equal("Bearer " + host.AccessToken, host.SeatInventory.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_OwnTrip_ShowsNoBookingIdsOrHoldEndTimes()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.OperatorPage);

        // Assert
        Assert.DoesNotContain(SeatStubs.BookingId.ToString(), page.Body!.TextContent);
        Assert.DoesNotContain("Booking ID", page.Body!.TextContent);
        Assert.DoesNotContain("Hold ends at", page.Body!.TextContent);
    }

    [Fact]
    public async Task Get_OwnTrip_OnlyFreeSeatsAndOwnBlocksCanBePicked()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.OperatorPage);

        // Assert
        Assert.Equal(new[] { "L1", "L2", "U2" }, SeatStubs.Pickable(page));
        Assert.Equal("AvailablePicks", page.QuerySelector("[data-pick][value='L1']")!.GetAttribute("name"));
        Assert.Equal("BlockedPicks", page.QuerySelector("[data-pick][value='U2']")!.GetAttribute("name"));
        Assert.NotNull(page.QuerySelector("[data-block]"));
        Assert.NotNull(page.QuerySelector("[data-unblock]"));
    }

    [Theory]
    [InlineData(true, false, "This trip was cancelled. Its seats will not be sold.")]
    [InlineData(false, true, "This trip has left.")]
    public async Task Get_CancelledOrDepartedTrip_ShowsNoticeAndNothingToPick(bool isCancelled, bool isDeparted, string notice)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(isCancelled, isDeparted)));
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.OperatorPage);

        // Assert
        Assert.StartsWith(notice, page.QuerySelector("[data-notice]")!.TextContent.Trim());
        Assert.Equal("Seats can no longer be blocked or unblocked.", page.QuerySelector("[data-no-changes]")!.TextContent);
        Assert.Empty(SeatStubs.Pickable(page));
        Assert.Null(page.QuerySelector("[data-block]"));
        Assert.Null(page.QuerySelector("[data-unblock]"));
    }

    [Fact]
    public async Task Get_AnotherCompanysTrip_ShowsForbiddenAndNoSeats()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => IdentityStubs.Problem(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.OperatorPage);

        // Assert
        Assert.Equal(Messages.ProblemForbidden, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-deck]"));
    }

    [Fact]
    public async Task Get_UnknownTrip_ShowsUnavailableWithTryAgainAndBackToMyTrips()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.OperatorPage);

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        IElement[] links = page.QuerySelectorAll("[data-page-alert] a").ToArray();
        Assert.Equal("Try again", links[0].TextContent);
        Assert.Equal(SeatStubs.OperatorPage, links[0].GetAttribute("href"));
        Assert.Equal("Back to my trips", links[1].TextContent);
        Assert.Equal("/operator/trips", links[1].GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-deck]"));
    }

    [Fact]
    public async Task Get_AddressNamesNoTrip_ShowsUnavailableWithoutCallingSeatInventory()
    {
        // Arrange
        using var host = new FleetTestHost();
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync("/operator/trips/not-a-trip/seats");

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Empty(host.SeatInventory.Requests);
    }

    [Fact]
    public async Task Get_SeatInventoryUnreachable_ShowsUpstreamMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => throw new HttpRequestException("connection refused"));
        FormClient browser = await host.SignInAsync("Operator");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.OperatorPage);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public async Task Get_NotAnOperator_ShowsNoPermissionWithoutCallingSeatInventory(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(SeatStubs.OperatorPage);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.SeatInventory.Requests);
    }

    private static string SeatText(IDocument page, string seatNo, string selector)
    {
        return page.QuerySelector($"[data-seat='{seatNo}'] {selector}")!.TextContent;
    }
}
