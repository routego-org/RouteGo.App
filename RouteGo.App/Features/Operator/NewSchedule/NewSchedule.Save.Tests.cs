using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.NewSchedule;

public sealed class NewScheduleSaveTests
{
    private const string SchedulesPath = "/schedules";
    private const string Save = "[data-save]";

    [Fact]
    public async Task Post_ValidSchedule_CreatesAndShowsScheduleWithConfirmation()
    {
        // Arrange
        using var host = new FleetTestHost();
        var created = new ScheduleStub { TripsGeneratedThrough = "2026-10-15" };
        host.Fleet.Map(HttpMethod.Post, SchedulesPath, () => FakeBackend.Json(created.ToWire(), HttpStatusCode.Created));
        host.Fleet.Map(HttpMethod.Get, $"/schedules/{TripStubs.ScheduleId}", created.Answer);
        host.Fleet.Map(HttpMethod.Get, $"/routes/{TripStubs.RouteId}", () => FakeBackend.Json(TripStubs.MumbaiPune("active")));
        NewScheduleTests.MapChoices(host);
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        BusFormDriver driver = await FillShivneriScheduleAsync(await NewScheduleTests.OpenFormAsync(client));
        string expectedBody = $"{{\"routeId\":\"{TripStubs.RouteId}\",\"busId\":\"{TripStubs.BusId}\",\"departureTime\":\"22:30\",\"daysOfWeek\":[\"monday\",\"wednesday\",\"friday\"],\"classFares\":[{{\"seatClass\":\"Lower berth\",\"fare\":1200}},{{\"seatClass\":\"Upper berth\",\"fare\":1000}}]}}";

        // Act
        HttpResponseMessage response = await driver.PressAsync(Save);
        string? sentBody = host.Fleet.LastRequestBody;
        IDocument result = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/operator/schedules/{TripStubs.ScheduleId}", response.Headers.Location!.OriginalString);
        Assert.Equal(expectedBody, sentBody);
        Assert.Equal("Schedule created. Trips are on sale until 15 Oct 2026.", result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task Post_NothingChosen_ShowsEachProblemAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await NewScheduleTests.OpenFormAsync(host);
        driver.Set("Input.DepartureTime", "24:00");

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Equal("Route is required.", driver.Text("[data-route-error]"));
        Assert.Equal("Bus is required.", driver.Text("[data-bus-error]"));
        Assert.Equal("Enter a time from 00:00 to 23:59, like 22:30.", driver.Text("[data-departure-time-error]"));
        Assert.Equal("Days is required.", driver.Text("[data-days-error]"));
        Assert.Equal("24:00", driver.Page.QuerySelector("[data-departure-time]")!.GetAttribute("value"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, SchedulesPath));
    }

    [Theory]
    [InlineData("49")]
    [InlineData("10001")]
    [InlineData("999.50")]
    public async Task Post_FareOutOfRange_ShowsFareMessageKeepsEverything(string fare)
    {
        // Arrange
        using var host = new FleetTestHost();
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await FillShivneriScheduleAsync(host);
        driver.Set("Input.Fares[1].Fare", fare);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Equal(new[] { "", Messages.ValidationFare }, NewScheduleTests.Texts(driver.Page, "[data-fare-error]"));
        Assert.Equal(fare, driver.Page.QuerySelectorAll("[data-fare-box]")[1].GetAttribute("value"));
        Assert.Equal("22:30", driver.Page.QuerySelector("[data-departure-time]")!.GetAttribute("value"));
        Assert.Equal(3, driver.Page.QuerySelectorAll("[data-days] input[checked]").Length);
        Assert.Equal(TripStubs.RouteId.ToString(), driver.Page.QuerySelector("[data-route] option[selected]")!.GetAttribute("value"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, SchedulesPath));
    }

    [Fact]
    public async Task Post_BusTripOverlap_ShowsConflictAndKeepsEverything()
    {
        // Arrange
        using var host = new FleetTestHost();
        var conflict = new { serviceDate = "2026-10-12", tripId = Guid.NewGuid(), departureAt = "2026-10-12T15:30:00Z" };
        var conflicts = new Dictionary<string, object> { ["conflicts"] = new[] { conflict } };
        host.Fleet.Map(HttpMethod.Post, SchedulesPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "BUS_TRIP_OVERLAP", conflicts));
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await FillShivneriScheduleAsync(host);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Contains("It is already busy on: 12 Oct 2026 (a trip leaving 9:00 PM IST).", driver.Text("[data-page-alert]"));
        Assert.Equal("1200", driver.Page.QuerySelectorAll("[data-fare-box]")[0].GetAttribute("value"));
        Assert.Equal("22:30", driver.Page.QuerySelector("[data-departure-time]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Post_ClassFaresMismatch_ReloadsSeatClassesKeepingMatchingFares()
    {
        // Arrange
        using var host = new FleetTestHost();
        string[] seatClasses = new[] { "Lower berth", "Upper berth" };
        host.Fleet.Map(HttpMethod.Get, $"/buses/{TripStubs.BusId}", () => FakeBackend.Json(new { busId = TripStubs.BusId, name = "Shivneri Sleeper", status = "active", seatClasses }));
        host.Fleet.Map(HttpMethod.Post, SchedulesPath, () =>
        {
            seatClasses = new[] { "LOWER BERTH", "Sleeper" };
            return IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "CLASS_FARES_MISMATCH");
        });
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await FillShivneriScheduleAsync(host);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Equal(Messages.ProblemClassFaresMismatch, driver.Text("[data-page-alert]"));
        Assert.Equal(new[] { "LOWER BERTH (₹)", "Sleeper (₹)" }, NewScheduleTests.Texts(driver.Page, "[data-fare-label]"));
        Assert.Equal("1200", driver.Page.QuerySelectorAll("[data-fare-box]")[0].GetAttribute("value"));
        Assert.Equal("", driver.Page.QuerySelectorAll("[data-fare-box]")[1].GetAttribute("value"));
    }

    [Theory]
    [InlineData("BUS_INACTIVE", "[data-bus-error]", "This bus is inactive. Activate the bus first, or choose another bus. Open the bus")]
    [InlineData("ROUTE_INACTIVE", "[data-route-error]", "This route is inactive. Choose another route.")]
    [InlineData("UNKNOWN_BUS", "[data-page-alert]", "We could not find this bus among your buses. We have loaded your buses again.")]
    [InlineData("UNKNOWN_ROUTE", "[data-page-alert]", "We could not find this route among your routes. We have loaded your routes again.")]
    [InlineData("OPERATOR_PROFILE_NOT_READY", "[data-page-alert]", "Your operator account is being set up. Please try again in a minute.")]
    public async Task Post_FleetRefuses_ShowsMessageWhereItBelongsKeepingTheForm(string code, string where, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, SchedulesPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, code));
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await FillShivneriScheduleAsync(host);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Equal(expected, CollapseSpaces(driver.Text(where)!));
        Assert.Equal("22:30", driver.Page.QuerySelector("[data-departure-time]")!.GetAttribute("value"));
        Assert.NotNull(driver.Page.QuerySelector("[data-schedule-form]"));
    }

    [Fact]
    public async Task Post_UnknownBus_ClearsBusChoice()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, SchedulesPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "UNKNOWN_BUS"));
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await FillShivneriScheduleAsync(host);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Null(driver.Page.QuerySelector("[data-bus] option[selected]"));
        Assert.Equal(TripStubs.RouteId.ToString(), driver.Page.QuerySelector("[data-route] option[selected]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Post_FleetRefusesTime_ShowsTimeMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, SchedulesPath, () => IdentityStubs.ValidationProblem("departureTime", "The departure time is not valid."));
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await FillShivneriScheduleAsync(host);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Equal(ScheduleFormValidator.TimeMessage, driver.Text("[data-departure-time-error]"));
    }

    [Fact]
    public async Task Post_FleetUnreachable_ShowsUpstreamKeepsEverything()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, SchedulesPath, () => throw new HttpRequestException("connection refused"));
        NewScheduleTests.MapChoices(host);
        BusFormDriver driver = await FillShivneriScheduleAsync(host);

        // Act
        await driver.PressAsync(Save);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, driver.Text("[data-page-alert]"));
        Assert.Equal("1000", driver.Page.QuerySelectorAll("[data-fare-box]")[1].GetAttribute("value"));
    }

    // The specs' example: Mumbai → Pune, "Shivneri Sleeper", 22:30 on Monday, Wednesday and Friday, ₹1,200 and ₹1,000.
    private static async Task<BusFormDriver> FillShivneriScheduleAsync(FleetTestHost host)
    {
        BusFormDriver driver = await NewScheduleTests.OpenFormAsync(host);
        return await FillShivneriScheduleAsync(driver);
    }

    private static async Task<BusFormDriver> FillShivneriScheduleAsync(BusFormDriver driver)
    {
        driver.Set("Input.BusId", TripStubs.BusId.ToString());
        await driver.PressAsync("[data-show-fares]");
        driver.Set("Input.RouteId", TripStubs.RouteId.ToString());
        driver.Set("Input.DepartureTime", "22:30");
        driver.Tick("Input.Days", "monday", true);
        driver.Tick("Input.Days", "wednesday", true);
        driver.Tick("Input.Days", "friday", true);
        driver.Set("Input.Fares[0].Fare", "1200");
        driver.Set("Input.Fares[1].Fare", "1000");
        return driver;
    }

    private static string CollapseSpaces(string text)
    {
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
