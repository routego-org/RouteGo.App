using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Trips;

public sealed class TripsTests
{
    internal const string PageAddress = "/operator/trips";
    internal const string TripsPath = "/trips";

    // The test clock reads 2 Oct 2026, 3:30 PM IST.
    internal static readonly Guid FrozenTripId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    internal static readonly Guid OpenTripId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    internal static readonly Guid DepartedTripId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    internal static readonly Guid CancelledTripId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task Get_Default_ListsTwoWeeksOfTripsWithDetails()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] columns = new[] { "Travel date", "Route", "Departs", "Arrives", "Bus", "Status", "Fares", "Cancellation reason", "Cancelled at", "" };
        string[] open = new[] { "3 Oct 2026", "Mumbai → Pune", "10:30 PM IST", "4 Oct 2026, 2:00 AM IST", "Shivneri Sleeper", "Scheduled", "Lower berth: ₹1,200, Upper berth: ₹1,000", "", "" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Contains(host.Fleet.Requests, request => request.RequestUri!.PathAndQuery == "/trips?from=2026-10-02&to=2026-10-15&page=1&pageSize=20");
        Assert.Contains(host.Fleet.Requests, request => request.RequestUri!.PathAndQuery == "/buses?page=1&pageSize=200");
        Assert.Equal("My trips", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("2026-10-02", page.QuerySelector("#filter-from")!.GetAttribute("value"));
        Assert.Equal("2026-10-15", page.QuerySelector("#filter-to")!.GetAttribute("value"));
        Assert.Equal(columns, Texts(page, "[data-trips] th"));
        Assert.Equal(open, Texts(page, $"[data-trip='{OpenTripId}'] td").Take(9).ToArray());
        Assert.Equal($"/buses/{TripStubs.BusId}", page.QuerySelector($"[data-trip='{OpenTripId}'] a")!.GetAttribute("href"));
        Assert.Equal("Bus breakdown at depot", Texts(page, $"[data-trip='{CancelledTripId}'] td")[7]);
        Assert.Equal("1 Oct 2026, 9:30 PM IST", Texts(page, $"[data-trip='{CancelledTripId}'] td")[8]);
        Assert.Equal("Showing 1–4 of 4 trips", page.QuerySelector("[data-trips-showing]")!.TextContent);
        Assert.Equal(new[] { "All", "Scheduled", "Departed", "Cancelled" }, Texts(page, "#filter-status option"));
        Assert.Equal(new[] { "All", "Shivneri Sleeper" }, Texts(page, "#filter-bus option"));
    }

    [Fact]
    public async Task Get_TripsInEachState_OfferOnlyActionsThatWork()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(new[] { "Change fares", "Cancel trip", "Seats", "Passengers" }, Texts(page, $"[data-trip='{OpenTripId}'] [data-trip-actions] a"));
        Assert.Equal(new[] { "Cancel trip", "Seats", "Passengers" }, Texts(page, $"[data-trip='{FrozenTripId}'] [data-trip-actions] a"));
        Assert.Equal("Fares are frozen (less than 6 hours to departure).", page.QuerySelector($"[data-trip='{FrozenTripId}'] [data-fares-frozen]")!.TextContent);
        Assert.Equal(new[] { "Seats", "Passengers" }, Texts(page, $"[data-trip='{DepartedTripId}'] [data-trip-actions] a"));
        Assert.Equal(new[] { "Seats", "Passengers" }, Texts(page, $"[data-trip='{CancelledTripId}'] [data-trip-actions] a"));
    }

    [Fact]
    public async Task Get_EveryTrip_LinksToItsSeatsWithRouteName()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(4, page.QuerySelectorAll("[data-trip-seats]").Length);
        Assert.Equal($"/operator/trips/{OpenTripId}/seats?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector($"[data-trip='{OpenTripId}'] [data-trip-seats]")!.GetAttribute("href"));
        Assert.Equal($"/operator/trips/{CancelledTripId}/seats?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector($"[data-trip='{CancelledTripId}'] [data-trip-seats]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_EveryTrip_LinksToItsPassengersWithRouteName()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(4, page.QuerySelectorAll("[data-trip-passengers]").Length);
        Assert.Equal($"/operator/trips/{OpenTripId}/passengers?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector($"[data-trip='{OpenTripId}'] [data-trip-passengers]")!.GetAttribute("href"));
        Assert.Equal($"/operator/trips/{CancelledTripId}/passengers?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector($"[data-trip='{CancelledTripId}'] [data-trip-passengers]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_FiltersChosen_SendsThemAndKeepsThemInAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => TripStubs.Page(45, 2, Trip(OpenTripId, "scheduled", "2026-10-13T17:00:00Z")));
        MapBuses(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?from=2026-10-12&to=2026-10-18&status=cancelled&busId={TripStubs.BusId}&pageNumber=2");

        // Assert
        Assert.Equal($"/trips?from=2026-10-12&to=2026-10-18&busId={TripStubs.BusId}&status=cancelled&page=2&pageSize=20", host.Fleet.Requests.First(request => request.RequestUri!.AbsolutePath == TripsPath).RequestUri!.PathAndQuery);
        Assert.Equal("cancelled", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Equal(TripStubs.BusId.ToString(), page.QuerySelector("#filter-bus option[selected]")!.GetAttribute("value"));
        Assert.Equal($"/operator/trips?from=2026-10-12&to=2026-10-18&status=cancelled&busId={TripStubs.BusId}&pageNumber=3", page.QuerySelectorAll("[data-trips-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_ScheduleIdFromSchedulePage_ListsThatScheduleWithWayBack()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?scheduleId={TripStubs.ScheduleId}");

        // Assert
        Assert.Contains(host.Fleet.Requests, request => request.RequestUri!.PathAndQuery == $"/trips?from=2026-10-02&to=2026-10-15&scheduleId={TripStubs.ScheduleId}&page=1&pageSize=20");
        Assert.StartsWith("Showing the trips of one schedule.", page.QuerySelector("[data-one-schedule]")!.TextContent);
        Assert.Equal("Show all trips", page.QuerySelector("[data-all-trips]")!.TextContent);
        Assert.Equal("/operator/trips", page.QuerySelector("[data-all-trips]")!.GetAttribute("href"));
        Assert.Equal(TripStubs.ScheduleId.ToString(), page.QuerySelector("[data-trips-filters] input[name='scheduleId']")!.GetAttribute("value"));
    }

    [Theory]
    [InlineData("2026-10-20", "2026-10-10")]
    [InlineData("2026-10-01", "2026-11-01")]
    [InlineData("not-a-date", "2026-10-10")]
    public async Task Get_DateRangeNotAllowed_ShowsRangeMessageWithoutListing(string from, string to)
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?from={from}&to={to}");

        // Assert
        Assert.Equal(Messages.ValidationDateRange, page.QuerySelector("[data-date-range-error]")!.TextContent);
        Assert.Equal(0, host.FleetCalls(HttpMethod.Get, TripsPath));
        Assert.Null(page.QuerySelector("[data-trips]"));
    }

    [Fact]
    public async Task Get_FleetRefusesDates_ShowsRangeMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => IdentityStubs.ValidationProblem("to", "The range is too long."));
        MapBuses(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(Messages.ValidationDateRange, page.QuerySelector("[data-date-range-error]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-trips]"));
    }

    [Fact]
    public async Task Get_NoTrips_ShowsFilteredEmptyMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, Array.Empty<object>());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("No trips match the current filters.", page.QuerySelector("[data-trips-empty]")!.TextContent);
    }

    [Fact]
    public async Task Get_BusesNotGiven_ListsTripsWithoutBusFilter()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => TripStubs.Page(4, 1, FourTrips()));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => IdentityStubs.Problem(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Null(page.QuerySelector("#filter-bus"));
        Assert.Equal(4, page.QuerySelectorAll("[data-trip]").Length);
        Assert.Null(page.QuerySelector("[data-page-alert]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    public async Task Get_FleetWillNotList_ShowsMessageInsteadOfContent(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => IdentityStubs.Problem(status, code));
        MapBuses(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-trips-filters]"));
        Assert.Null(page.QuerySelector("[data-trips]"));
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

    /// <summary>
    /// Tonight's trip leaving in 5 hours (frozen), tomorrow's trip (open), this morning's trip
    /// (departed) and a cancelled one.
    /// </summary>
    internal static object[] FourTrips()
    {
        return new[]
        {
            Trip(DepartedTripId, "departed", "2026-10-02T02:00:00Z"),
            Trip(FrozenTripId, "scheduled", "2026-10-02T15:00:00Z"),
            Trip(OpenTripId, "scheduled", "2026-10-03T17:00:00Z"),
            Trip(CancelledTripId, "cancelled", "2026-10-04T17:00:00Z"),
        };
    }

    internal static void MapFleet(FleetTestHost host, object[] trips)
    {
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => TripStubs.Page(trips.Length, 1, trips));
        MapBuses(host);
    }

    internal static void MapBuses(FleetTestHost host)
    {
        host.Fleet.Map(HttpMethod.Get, "/buses", () => TripStubs.Page(1, 1, TripStubs.ShivneriSleeper("active")));
    }

    // Fleet's Trip shape: Mumbai → Pune on "Shivneri Sleeper", arriving 3 h 30 min after leaving.
    internal static object Trip(Guid tripId, string status, string departureAt)
    {
        var departs = DateTimeOffset.Parse(departureAt, System.Globalization.CultureInfo.InvariantCulture);
        bool isCancelled = status == "cancelled";
        return new
        {
            tripId,
            scheduleId = TripStubs.ScheduleId,
            operatorId = TripStubs.OperatorId,
            operatorName = "BusCo Travels",
            routeId = TripStubs.RouteId,
            source = new { cityId = Guid.NewGuid(), name = "Mumbai" },
            destination = new { cityId = Guid.NewGuid(), name = "Pune" },
            busId = TripStubs.BusId,
            busName = "Shivneri Sleeper",
            serviceDate = departs.ToOffset(new TimeSpan(5, 30, 0)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            departureAt = departs,
            arrivalAt = departs.AddMinutes(210),
            status,
            classFares = new object[] { new { seatClass = "Lower berth", fare = 1200 }, new { seatClass = "Upper berth", fare = 1000 } },
            cancelReason = isCancelled ? "Bus breakdown at depot" : null,
            cancelledAt = isCancelled ? new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero) : (DateTimeOffset?)null,
        };
    }

    internal static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
