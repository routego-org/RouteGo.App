using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.EditSchedule;

public sealed class EditScheduleSaveTests
{
    private const string Save = "[data-save]";

    [Fact]
    public async Task PostSave_FaresOnlyChanged_SavesWithoutAskingAndShowsCounts()
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub();
        host.Fleet.Map(HttpMethod.Put, EditScheduleTests.SchedulePath, () => Change(schedule, 0, 0, 5));
        EditScheduleTests.MapFleet(host, schedule, "active", "active");
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        BusFormDriver driver = await OpenFormAsync(client);
        driver.Set("Input.Fares[0].Fare", "1400");
        driver.Set("Input.Fares[1].Fare", "1100");
        string expectedBody = $"{{\"routeId\":\"{TripStubs.RouteId}\",\"busId\":\"{TripStubs.BusId}\",\"departureTime\":\"22:30\",\"daysOfWeek\":[\"monday\",\"wednesday\",\"friday\"],\"classFares\":[{{\"seatClass\":\"Lower berth\",\"fare\":1400}},{{\"seatClass\":\"Upper berth\",\"fare\":1100}}]}}";

        // Act
        HttpResponseMessage response = await driver.PressAsync(Save);
        string? sentBody = host.Fleet.LastRequestBody;
        IDocument result = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/operator/schedules/{TripStubs.ScheduleId}", response.Headers.Location!.OriginalString);
        Assert.Equal(expectedBody, sentBody);
        Assert.Equal("Schedule saved. 0 new trips, 0 trips cancelled, 5 trips with new fares.", result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task PostSave_TimeChanged_AsksFirstAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        EditScheduleTests.MapFleet(host, new ScheduleStub(), "active", "active");
        BusFormDriver driver = await EditScheduleTests.OpenFormAsync(host);
        driver.Set("Input.DepartureTime", "23:00");

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Equal(Messages.ConfirmTripsWillBeCancelled, driver.Text("[data-confirm-question]"));
        Assert.Equal("Save and change trips", driver.Text("[data-confirm-save]"));
        Assert.Equal("Go back", driver.Text("[data-go-back]"));
        Assert.Equal("23:00", driver.Page.QuerySelector("[data-departure-time]")!.GetAttribute("value"));
        Assert.Null(driver.Page.QuerySelector(Save));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Put, EditScheduleTests.SchedulePath));
    }

    [Fact]
    public async Task PostSave_TimeChangedAndConfirmed_SavesAndShowsCounts()
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub();
        host.Fleet.Map(HttpMethod.Put, EditScheduleTests.SchedulePath, () => Change(schedule, 6, 6, 0));
        EditScheduleTests.MapFleet(host, schedule, "active", "active");
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        BusFormDriver driver = await OpenFormAsync(client);
        driver.Set("Input.DepartureTime", "23:00");
        await driver.PressAsync(Save);

        // Act
        HttpResponseMessage response = await driver.PressAsync("[data-confirm-save]");
        string? sentBody = host.Fleet.LastRequestBody;
        IDocument result = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("\"departureTime\":\"23:00\"", sentBody);
        Assert.Equal("Schedule saved. 6 new trips, 6 trips cancelled, 0 trips with new fares.", result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task PostBack_FromQuestion_ReturnsToFormKeepingEverything()
    {
        // Arrange
        using var host = new FleetTestHost();
        EditScheduleTests.MapFleet(host, new ScheduleStub(), "active", "active");
        BusFormDriver driver = await EditScheduleTests.OpenFormAsync(host);
        driver.Tick("Input.Days", "sunday", true);
        driver.Set("Input.Fares[0].Fare", "1300");
        await driver.PressAsync(Save);

        // Act
        await driver.PressAsync("[data-go-back]");

        // Assert
        Assert.Null(driver.Page.QuerySelector("[data-confirm]"));
        Assert.NotNull(driver.Page.QuerySelector(Save));
        Assert.Equal(4, driver.Page.QuerySelectorAll("[data-days] input[checked]").Length);
        Assert.Equal("1300", driver.Page.QuerySelectorAll("[data-fare-box]")[0].GetAttribute("value"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Put, EditScheduleTests.SchedulePath));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("inactive")]
    public async Task PostSave_NothingChanged_SavesWithoutAsking(string routeStatus)
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub();
        host.Fleet.Map(HttpMethod.Put, EditScheduleTests.SchedulePath, () => Change(schedule, 0, 0, 0));
        EditScheduleTests.MapFleet(host, schedule, routeStatus, "active");
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        BusFormDriver driver = await OpenFormAsync(client);

        // Act
        HttpResponseMessage response = await driver.PressAsync(Save);
        IDocument result = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Put, EditScheduleTests.SchedulePath));
        Assert.Equal("Schedule saved. No trips needed to change.", result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task PostSave_ScheduleStoppedMeanwhile_ShowsStoppedWithoutForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Put, EditScheduleTests.SchedulePath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "SCHEDULE_INACTIVE"));
        EditScheduleTests.MapFleet(host, new ScheduleStub(), "active", "active");
        BusFormDriver driver = await EditScheduleTests.OpenFormAsync(host);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.StartsWith(Messages.ProblemScheduleInactive, driver.Text("[data-page-alert]"));
        Assert.Equal("Back to the schedule", driver.Text("[data-page-alert] a"));
        Assert.Null(driver.Page.QuerySelector("[data-schedule-form]"));
    }

    [Fact]
    public async Task PostSave_BusTripOverlap_ShowsConflictAndKeepsEverything()
    {
        // Arrange
        using var host = new FleetTestHost();
        var conflict = new { serviceDate = "2026-10-12", tripId = Guid.NewGuid(), departureAt = "2026-10-12T15:30:00Z" };
        var conflicts = new Dictionary<string, object> { ["conflicts"] = new[] { conflict } };
        host.Fleet.Map(HttpMethod.Put, EditScheduleTests.SchedulePath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "BUS_TRIP_OVERLAP", conflicts));
        EditScheduleTests.MapFleet(host, new ScheduleStub(), "active", "active");
        BusFormDriver driver = await EditScheduleTests.OpenFormAsync(host);
        driver.Set("Input.Fares[1].Fare", "1100");

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Contains("12 Oct 2026 (a trip leaving 9:00 PM IST)", driver.Text("[data-page-alert]"));
        Assert.Equal("1100", driver.Page.QuerySelectorAll("[data-fare-box]")[1].GetAttribute("value"));
        Assert.NotNull(driver.Page.QuerySelector("[data-schedule-form]"));
    }

    // Fleet's ScheduleChange answer.
    private static HttpResponseMessage Change(ScheduleStub schedule, int created, int cancelled, int repriced)
    {
        return FakeBackend.Json(new { schedule = schedule.ToWire(), createdTripCount = created, cancelledTripCount = cancelled, repricedTripCount = repriced });
    }

    private static async Task<BusFormDriver> OpenFormAsync(HttpClient client)
    {
        IDocument page = await new FormClient(client).GetPageAsync(EditScheduleTests.PageAddress);
        return new BusFormDriver(client, page);
    }
}
