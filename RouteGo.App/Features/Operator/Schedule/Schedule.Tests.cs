using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Schedule;

public sealed class ScheduleTests
{
    private static readonly string _pageAddress = $"/operator/schedules/{TripStubs.ScheduleId}";
    private static readonly string _schedulePath = $"/schedules/{TripStubs.ScheduleId}";
    private static readonly string _routePath = $"/routes/{TripStubs.RouteId}";
    private static readonly string _busPath = $"/buses/{TripStubs.BusId}";

    [Fact]
    public async Task Get_ActiveSchedule_ShowsDetailsAndActions()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapSchedule(host, new ScheduleStub());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] stops = new[] { "Mumbai — 0 min", "Lonavala — 1 h 50 min", "Pune — 3 h 30 min" };
        string[] actions = new[] { "Change schedule", "Stop schedule", "See trips" };

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(_schedulePath, host.Fleet.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("Mumbai → Pune via Lonavala, 10:30 PM IST", page.QuerySelector("[data-schedule-name]")!.TextContent);
        Assert.Equal("Mumbai → Pune via Lonavala", page.QuerySelector("[data-route-name]")!.TextContent);
        Assert.Equal(stops, Texts(page, "[data-stop]"));
        Assert.Equal("Shivneri Sleeper", page.QuerySelector("[data-bus] a")!.TextContent);
        Assert.Equal($"/operator/buses/{TripStubs.BusId}", page.QuerySelector("[data-bus] a")!.GetAttribute("href"));
        Assert.Equal("10:30 PM IST", page.QuerySelector("[data-departure-time]")!.TextContent);
        Assert.Equal("Monday, Wednesday, Friday", page.QuerySelector("[data-days]")!.TextContent);
        Assert.Equal("Lower berth: ₹1,200, Upper berth: ₹1,000", page.QuerySelector("[data-fares]")!.TextContent);
        Assert.Equal("Active", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("16 Oct 2026", page.QuerySelector("[data-on-sale-until]")!.TextContent);
        Assert.Equal("1 Oct 2026, 10:00 AM IST", page.QuerySelector("[data-created-at]")!.TextContent);
        Assert.Equal("2 Oct 2026, 10:30 AM IST", page.QuerySelector("[data-updated-at]")!.TextContent);
        Assert.Equal(actions, Texts(page, "[data-schedule-actions] a"));
        Assert.Equal($"/operator/schedules/{TripStubs.ScheduleId}/edit", page.QuerySelector("[data-edit]")!.GetAttribute("href"));
        Assert.Equal($"/operator/trips?scheduleId={TripStubs.ScheduleId}", page.QuerySelector("[data-see-trips]")!.GetAttribute("href"));
        Assert.Equal("Back to my schedules", page.QuerySelector("[data-back]")!.TextContent);
        Assert.Equal("/operator/schedules", page.QuerySelector("[data-back]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_ActiveSchedule_OffersDatesFromTodayToSixtyDaysAhead()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapSchedule(host, new ScheduleStub());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Put more trips on sale", page.QuerySelector("[data-extend] h2")!.TextContent);
        Assert.Equal("2026-10-02", page.QuerySelector("[data-end-date]")!.GetAttribute("min"));
        Assert.Equal("2026-12-01", page.QuerySelector("[data-end-date]")!.GetAttribute("max"));
        Assert.Equal("You can put trips on sale up to 60 days ahead (until 1 Dec 2026).", page.QuerySelector("[data-end-date-hint]")!.TextContent);
        Assert.Equal("Put trips on sale", page.QuerySelector("[data-extend-form] button")!.TextContent);
    }

    [Fact]
    public async Task Get_InactiveSchedule_OffersRestartAndTripsOnly()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapSchedule(host, new ScheduleStub { Status = "inactive" });
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress + "?stop=true");

        // Assert
        Assert.Equal("Inactive", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("Restart schedule", page.QuerySelector("[data-restart-form] button")!.TextContent);
        Assert.Equal(new[] { "See trips" }, Texts(page, "[data-schedule-actions] a"));
        Assert.Null(page.QuerySelector("[data-extend]"));
        Assert.Null(page.QuerySelector("[data-confirm]"));
    }

    [Fact]
    public async Task Get_EveryDayAndBusNotGiven_ShowsEveryDayUnknownBusAndFleetFareOrder()
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub { Days = new[] { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" } };
        host.Fleet.Map(HttpMethod.Get, _schedulePath, schedule.Answer);
        host.Fleet.Map(HttpMethod.Get, _routePath, () => FakeBackend.Json(TripStubs.MumbaiPune("inactive")));
        host.Fleet.Map(HttpMethod.Get, _busPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Every day", page.QuerySelector("[data-days]")!.TextContent);
        Assert.Equal("Unknown bus", page.QuerySelector("[data-bus]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-bus] a"));
        Assert.Equal("Upper berth: ₹1,000, Lower berth: ₹1,200", page.QuerySelector("[data-fares]")!.TextContent);
    }

    [Fact]
    public async Task Get_RouteNotGiven_ShowsUnknownRouteWithoutStops()
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub();
        host.Fleet.Map(HttpMethod.Get, _schedulePath, schedule.Answer);
        host.Fleet.Map(HttpMethod.Get, _routePath, () => IdentityStubs.Problem(HttpStatusCode.Forbidden, "FORBIDDEN"));
        host.Fleet.Map(HttpMethod.Get, _busPath, () => FakeBackend.Json(TripStubs.ShivneriSleeper("active")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Unknown route, 10:30 PM IST", page.QuerySelector("[data-schedule-name]")!.TextContent);
        Assert.Equal("Unknown route", page.QuerySelector("[data-route-name]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-stops]"));
        Assert.Null(page.QuerySelector("[data-page-alert]"));
    }

    [Fact]
    public async Task Get_ScheduleNotFound_ShowsNotFoundWithLinkBack()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _schedulePath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith("We couldn't find that schedule. It may have been removed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Back to my schedules", page.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal("/operator/schedules", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-schedule-fields]"));
        Assert.Single(host.Fleet.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "This part of RouteGo is not available right now. Please try again in a few minutes.")]
    public async Task Get_FleetRefuses_ShowsMessageInsteadOfSchedule(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _schedulePath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-schedule-name]"));
        Assert.Null(page.QuerySelector("[data-schedule-fields]"));
    }

    [Fact]
    public async Task Get_FleetAnswersUnauthorized_EndsSession()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _schedulePath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage response = await client.GetAsync(_pageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/auth/login?", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public async Task Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(_pageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Fleet.Requests);
    }

    /// <summary>Answers the schedule from the stub, with the example route and bus.</summary>
    internal static void MapSchedule(FleetTestHost host, ScheduleStub schedule)
    {
        host.Fleet.Map(HttpMethod.Get, _schedulePath, schedule.Answer);
        host.Fleet.Map(HttpMethod.Get, _routePath, () => FakeBackend.Json(TripStubs.MumbaiPune("active")));
        host.Fleet.Map(HttpMethod.Get, _busPath, () => FakeBackend.Json(TripStubs.ShivneriSleeper("active")));
    }

    internal static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
