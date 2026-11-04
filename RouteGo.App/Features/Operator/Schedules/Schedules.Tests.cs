using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Schedules;

public sealed class SchedulesTests
{
    private const string PageAddress = "/operator/schedules";
    private const string SchedulesPath = "/schedules";
    private const string ChoiceQuery = "?page=1&pageSize=200";

    [Fact]
    public async Task Get_Operator_ListsSchedulesWithNamesAndLinks()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => TripStubs.Page(1, 1, BusOrderSchedule().ToWire()));
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] columns = new[] { "Route", "Bus", "Departure time", "Days", "Fares", "Status", "Trips on sale until" };
        string[] row = new[] { "Mumbai → Pune via Lonavala", "Shivneri Sleeper", "10:30 PM IST", "Monday, Wednesday, Friday", "Lower berth: ₹1,200, Upper berth: ₹1,000", "Active", "16 Oct 2026" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("/schedules?page=1&pageSize=20", host.Fleet.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal(1, host.Fleet.Requests.Count(request => request.RequestUri!.PathAndQuery == "/routes" + ChoiceQuery));
        Assert.Equal(1, host.Fleet.Requests.Count(request => request.RequestUri!.PathAndQuery == "/buses" + ChoiceQuery));
        Assert.Equal("My schedules", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(columns, Texts(page, "[data-schedules] th"));
        Assert.Equal(row, Texts(page, "[data-schedule] td"));
        Assert.Equal($"/operator/schedules/{TripStubs.ScheduleId}", page.QuerySelector("[data-schedule] a")!.GetAttribute("href"));
        Assert.Equal("Add a schedule", page.QuerySelector("[data-add-schedule]")!.TextContent);
        Assert.Equal("/operator/schedules/new", page.QuerySelector("[data-add-schedule]")!.GetAttribute("href"));
        Assert.Equal("Showing 1–1 of 1 schedules", page.QuerySelector("[data-schedules-showing]")!.TextContent);
        Assert.Equal(new[] { "All", "Active", "Inactive" }, Texts(page, "#filter-status option"));
        Assert.Equal(new[] { "All", "Mumbai → Pune via Lonavala", "Pune → Nashik" }, Texts(page, "#filter-route option"));
        Assert.Equal(new[] { "All", "Shivneri Sleeper", "Volvo Seater" }, Texts(page, "#filter-bus option"));
        Assert.Equal("Show", page.QuerySelector("[data-schedules-filters] button")!.TextContent);
    }

    [Fact]
    public async Task Get_RouteAndStatusChosen_SendsFiltersAndKeepsThemInAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => TripStubs.Page(45, 2, BusOrderSchedule().ToWire()));
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?status=active&routeId={TripStubs.RouteId}&pageNumber=2");

        // Assert
        Assert.Equal($"/schedules?status=active&routeId={TripStubs.RouteId}&page=2&pageSize=20", host.Fleet.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("active", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Equal(TripStubs.RouteId.ToString(), page.QuerySelector("#filter-route option[selected]")!.GetAttribute("value"));
        Assert.Equal("", page.QuerySelector("#filter-bus option[selected]")!.GetAttribute("value"));
        Assert.Equal("Showing 21–21 of 45 schedules", page.QuerySelector("[data-schedules-showing]")!.TextContent);
        Assert.Equal($"/operator/schedules?status=active&routeId={TripStubs.RouteId}&pageNumber=3", page.QuerySelectorAll("[data-schedules-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_BusIdFromBusPage_SendsBusIdAndShowsThatBusChosen()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => TripStubs.Page(1, 1, BusOrderSchedule().ToWire()));
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?busId={TripStubs.BusId}");

        // Assert
        Assert.Equal($"/schedules?busId={TripStubs.BusId}&page=1&pageSize=20", host.Fleet.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("Shivneri Sleeper", page.QuerySelector("#filter-bus option[selected]")!.TextContent);
    }

    [Fact]
    public async Task Get_NoSchedulesAtAll_ShowsFirstScheduleInvitation()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => TripStubs.Page(0, 1));
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("You have no schedules yet. Add a schedule to put a bus on a route and start selling trips.", page.QuerySelector("[data-schedules-empty]")!.TextContent);
        Assert.NotNull(page.QuerySelector("[data-add-schedule]"));
        Assert.Null(page.QuerySelector("[data-schedules]"));
    }

    [Fact]
    public async Task Get_FilterMatchesNothing_ShowsFilteredEmptyMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => TripStubs.Page(0, 1));
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?busId={TripStubs.SecondBusId}");

        // Assert
        Assert.Equal("No schedules match the current filters.", page.QuerySelector("[data-schedules-empty]")!.TextContent);
    }

    [Fact]
    public async Task Get_FleetRefusesFilter_ShowsAllSchedulesWithProblemTitle()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.MapPathAndQuery(HttpMethod.Get, "/schedules?status=retired&page=1&pageSize=20", () => IdentityStubs.ValidationProblem("status", "The status is not valid."));
        host.Fleet.MapPathAndQuery(HttpMethod.Get, "/schedules?page=1&pageSize=20", () => TripStubs.Page(1, 1, BusOrderSchedule().ToWire()));
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=retired");

        // Assert
        Assert.Equal("One or more validation errors occurred.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Single(page.QuerySelectorAll("[data-schedule]"));
    }

    [Fact]
    public async Task Get_RoutesAndBusesNotGiven_ShowsUnknownNamesWithoutThoseFilters()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => TripStubs.Page(1, 1, BusOrderSchedule().ToWire()));
        host.Fleet.Map(HttpMethod.Get, "/routes", () => IdentityStubs.Problem(HttpStatusCode.Forbidden, "FORBIDDEN"));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => IdentityStubs.ValidationProblem("pageSize", "Too big."));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(new[] { "Unknown route", "Unknown bus" }, Texts(page, "[data-schedule] td").Take(2).ToArray());
        Assert.Null(page.QuerySelector("#filter-route"));
        Assert.Null(page.QuerySelector("#filter-bus"));
        Assert.NotNull(page.QuerySelector("#filter-status"));
        Assert.Null(page.QuerySelector("[data-page-alert]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    public async Task Get_FleetWillNotList_ShowsMessageInsteadOfContent(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-schedules-filters]"));
        Assert.Null(page.QuerySelector("[data-add-schedule]"));
        Assert.Single(host.Fleet.Requests);
    }

    [Fact]
    public async Task Get_FleetUnreachable_ShowsUpstreamMessageKeepsFilter()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulesPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=inactive");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("inactive", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-schedules]"));
    }

    [Fact]
    public async Task Get_Customer_ShowsNoPermissionWithoutCallingFleet()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleCustomer);

        // Act
        HttpResponseMessage response = await client.GetAsync(PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Fleet.Requests);
    }

    // The list carries no seat-class order, so Fleet's fare order is shown; here it is the bus's.
    private static ScheduleStub BusOrderSchedule()
    {
        return new ScheduleStub
        {
            ClassFares = new object[]
            {
                new { seatClass = "Lower berth", fare = 1200 },
                new { seatClass = "Upper berth", fare = 1000 },
            },
        };
    }

    private static void MapChoices(FleetTestHost host)
    {
        host.Fleet.Map(HttpMethod.Get, "/routes", () => TripStubs.Page(2, 1, TripStubs.MumbaiPune("active"), TripStubs.PuneNashik("inactive")));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => TripStubs.Page(2, 1, TripStubs.ShivneriSleeper("active"), TripStubs.VolvoSeater("inactive")));
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
