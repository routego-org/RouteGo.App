using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.SeatAllocations;

public sealed class SeatAllocationsTests
{
    [Fact]
    public async Task Get_AnyTrip_ShowsSeatsWithBookingIdsHoldEndsAndLinks()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        FormClient browser = await host.SignInAsync("Admin");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.AdminPage);

        // Assert
        Assert.Equal("Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal(new[] { $"Booking ID: {SeatStubs.StuckBookingId}", "Hold ends at: 2 Oct 2026, 12:30 PM IST" }, Lines(page, "3A"));
        Assert.Equal(new[] { $"Booking ID: {SeatStubs.RunningBookingId}", "Hold ends at: 2 Oct 2026, 3:36 PM IST" }, Lines(page, "5C"));
        Assert.Equal(new[] { $"Booking ID: {SeatStubs.PaidBookingId}" }, Lines(page, "4C"));
        Assert.Equal($"/admin/bookings/{SeatStubs.StuckBookingId}", page.QuerySelector("[data-seat='3A'] [data-line='Booking ID'] a")!.GetAttribute("href"));
        Assert.Equal(new[] { "Blocked by: Operator" }, Lines(page, "L1"));
        Assert.Empty(Lines(page, "L2"));
        Assert.Equal("Available: 1 · On hold: 4 · Booked: 1 · Blocked: 1", page.QuerySelector("[data-counts]")!.TextContent);
        Assert.Equal($"/trips/{SeatStubs.TripId}/seats?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector("[data-public-seat-map]")!.GetAttribute("href"));
        Assert.Equal("Back to trips", page.QuerySelector("[data-back-to-trips]")!.TextContent);
        Assert.Equal("/admin/trips", page.QuerySelector("[data-back-to-trips]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-pick]"));
        Assert.Equal("Bearer " + host.AccessToken, host.SeatInventory.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_StuckHold_StandsOutWithOneCancelHold()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        FormClient browser = await host.SignInAsync("Admin");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.AdminPage);

        // Assert
        Assert.Equal("1 stuck hold on this trip.", page.QuerySelector("[data-stuck-count]")!.TextContent);
        Assert.Equal("Stuck: hold time ended at 2 Oct 2026, 12:30 PM IST.", page.QuerySelector("[data-seat='3A'] [data-stuck]")!.TextContent);
        Assert.Equal("Stuck: hold time ended at 2 Oct 2026, 12:30 PM IST.", page.QuerySelector("[data-seat='3B'] [data-stuck]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-seat='5C'] [data-stuck]"));
        Assert.Null(page.QuerySelector("[data-seat='6D'] [data-stuck]"));
        IElement[] cancels = page.QuerySelectorAll("[data-cancel-hold]").ToArray();
        Assert.Single(cancels);
        Assert.Equal("Cancel hold", cancels[0].TextContent);
        Assert.Contains($"cancel={SeatStubs.StuckBookingId}", cancels[0].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_NoStuckHold_ShowsNoStuckLine()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        FormClient browser = await host.SignInAsync("Admin");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.AdminPage);

        // Assert
        Assert.Null(page.QuerySelector("[data-stuck-holds]"));
        Assert.Null(page.QuerySelector("[data-cancel-hold]"));
    }

    [Theory]
    [InlineData(false, false, "The seats go back on sale.")]
    [InlineData(true, false, "The trip was cancelled, so the seats will be blocked.")]
    [InlineData(false, true, "The seats will be free again.")]
    public async Task Get_CancelHoldLink_AsksNamingBookingSeatsAndOutcome(bool isCancelled, bool isDeparted, string outcome)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(isCancelled, isDeparted)));
        FormClient browser = await host.SignInAsync("Admin");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.AdminPage + "&cancel=" + SeatStubs.StuckBookingId);

        // Assert
        Assert.Equal($"Cancel the hold of booking {SeatStubs.StuckBookingId} on seats 3A, 3B? {outcome} This cannot be undone.", page.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Reason", page.QuerySelector("label[for='CancelInput_Reason']")!.TextContent);
        Assert.Equal("Cancel hold", page.QuerySelector("[data-confirm-cancel]")!.TextContent);
        Assert.Equal("Go back", page.QuerySelector("[data-go-back]")!.TextContent);
        Assert.Equal(0, host.SeatInventoryCalls(HttpMethod.Post, SeatStubs.CancelHoldPath));
    }

    [Fact]
    public async Task Get_CancelLinkForRunningHold_AsksNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        FormClient browser = await host.SignInAsync("Admin");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.AdminPage + "&cancel=" + SeatStubs.RunningBookingId);

        // Assert
        Assert.Null(page.QuerySelector("[data-confirm]"));
    }

    [Fact]
    public async Task Get_UnknownTrip_ShowsUnavailableWithTryAgainAndBackToTrips()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync("Admin");

        // Act
        IDocument page = await browser.GetPageAsync(SeatStubs.AdminPage);

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        IElement[] links = page.QuerySelectorAll("[data-page-alert] a").ToArray();
        Assert.Equal("Try again", links[0].TextContent);
        Assert.Equal(SeatStubs.AdminPage, links[0].GetAttribute("href"));
        Assert.Equal("Back to trips", links[1].TextContent);
        Assert.Equal("/admin/trips", links[1].GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-deck]"));
    }

    [Fact]
    public async Task Get_Operator_ShowsNoPermissionWithoutCallingSeatInventory()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync("Operator");

        // Act
        HttpResponseMessage response = await client.GetAsync(SeatStubs.AdminPage);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.SeatInventory.Requests);
    }

    private static string[] Lines(IDocument page, string seatNo)
    {
        return page.QuerySelectorAll($"[data-seat='{seatNo}'] [data-line]").Select(line => line.TextContent).ToArray();
    }
}
