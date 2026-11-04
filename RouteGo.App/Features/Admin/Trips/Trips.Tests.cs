using System.Globalization;
using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.Trips;

public sealed class TripsTests
{
    private const string PageAddress = "/admin/trips";
    private const string TripsPath = "/admin/trips";
    private const string CitiesPath = "/cities";
    private const string MumbaiId = "aaaaaaaa-0000-4000-8000-000000000001";
    private const string PuneId = "aaaaaaaa-0000-4000-8000-000000000002";

    // The test clock reads 2 Oct 2026, 3:30 PM IST.
    private static readonly Guid _scheduledTripId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid _cancelledTripId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    [Fact]
    public async Task Get_Default_ListsTwoWeeksOfEveryCompanysTripsReadOnly()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, TwoTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        string[] columns = new[] { "Company name", "Travel date", "Route", "Departs", "Arrives", "Bus", "Status", "Fares", "Cancellation reason", "Cancelled at", "" };
        string[] scheduled = new[] { "BusCo Travels", "3 Oct 2026", "Mumbai → Pune", "10:30 PM IST", "4 Oct 2026, 2:00 AM IST", "Shivneri Sleeper", "Scheduled", "Lower berth: ₹1,200, Upper berth: ₹1,000", "", "", "Seats Bookings" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Get, CitiesPath));
        Assert.Equal("/admin/trips?from=2026-10-02&to=2026-10-15&page=1&pageSize=20", TripsRequest(host));
        Assert.Equal("Trips", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("2026-10-02", page.QuerySelector("#filter-from")!.GetAttribute("value"));
        Assert.Equal("2026-10-15", page.QuerySelector("#filter-to")!.GetAttribute("value"));
        Assert.Equal(columns, Texts(page, "[data-trips] th"));
        Assert.Equal(scheduled, Texts(page, $"[data-trip='{_scheduledTripId}'] td"));
        Assert.Equal($"/buses/{TripStubs.BusId}", page.QuerySelector($"[data-trip='{_scheduledTripId}'] a")!.GetAttribute("href"));
        Assert.Equal("Bus breakdown at depot", Texts(page, $"[data-trip='{_cancelledTripId}'] td")[8]);
        Assert.Equal("1 Oct 2026, 9:30 PM IST", Texts(page, $"[data-trip='{_cancelledTripId}'] td")[9]);
        Assert.Equal(new[] { "Shivneri Sleeper", "Seats", "Bookings" }, Texts(page, $"[data-trip='{_scheduledTripId}'] a"));
        Assert.Equal($"/admin/trips/{_scheduledTripId}/seats?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector($"[data-trip='{_scheduledTripId}'] [data-trip-seats]")!.GetAttribute("href"));
        Assert.Equal($"/admin/trips/{_cancelledTripId}/seats?route=Mumbai%20%E2%86%92%20Pune", page.QuerySelector($"[data-trip='{_cancelledTripId}'] [data-trip-seats]")!.GetAttribute("href"));
        Assert.Equal($"/admin/bookings?tripId={_scheduledTripId}", page.QuerySelector($"[data-trip='{_scheduledTripId}'] [data-trip-bookings]")!.GetAttribute("href"));
        Assert.Empty(page.QuerySelectorAll("[data-trips] button"));
        Assert.Equal("Showing 1–2 of 2 trips", page.QuerySelector("[data-trips-showing]")!.TextContent);
        Assert.Equal(new[] { "All", "Scheduled", "Departed", "Cancelled" }, Texts(page, "#filter-status option"));
        Assert.Equal(new[] { "All", "Mumbai, Maharashtra", "Pune, Maharashtra" }, Texts(page, "#filter-source-city option"));
        Assert.Equal(new[] { "All", "Mumbai, Maharashtra", "Pune, Maharashtra" }, Texts(page, "#filter-destination-city option"));
        Assert.Null(page.QuerySelector("[data-one-company]"));
    }

    [Fact]
    public async Task Get_RouteAndDatesChosen_SendsThemAndKeepsThemInAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => TripStubs.Page(45, 2, Trip(_scheduledTripId, "scheduled")));
        MapCities(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?sourceCityId={MumbaiId}&destinationCityId={PuneId}&from=2026-10-12&to=2026-10-12&status=scheduled&pageNumber=2");

        // Assert
        Assert.Equal($"/admin/trips?sourceCityId={MumbaiId}&destinationCityId={PuneId}&from=2026-10-12&to=2026-10-12&status=scheduled&page=2&pageSize=20", TripsRequest(host));
        Assert.Equal(MumbaiId, page.QuerySelector("#filter-source-city option[selected]")!.GetAttribute("value"));
        Assert.Equal(PuneId, page.QuerySelector("#filter-destination-city option[selected]")!.GetAttribute("value"));
        Assert.Equal("scheduled", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Equal(
            $"/admin/trips?sourceCityId={MumbaiId}&destinationCityId={PuneId}&from=2026-10-12&to=2026-10-12&status=scheduled&pageNumber=3",
            page.QuerySelectorAll("[data-trips-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_OneCompanyFromOperatorsPage_SearchesThatCompanyWithWayBack()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, TwoTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        string operatorId = TripStubs.OperatorId.ToString();

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?operatorId={operatorId}&operatorName=BusCo%20Travels&status=cancelled&from=2026-10-01&to=2026-10-08");

        // Assert
        Assert.Equal($"/admin/trips?operatorId={operatorId}&from=2026-10-01&to=2026-10-08&status=cancelled&page=1&pageSize=20", TripsRequest(host));
        Assert.StartsWith("Company: BusCo Travels", page.QuerySelector("[data-one-company]")!.TextContent);
        Assert.Equal("Show all companies", page.QuerySelector("[data-all-companies]")!.TextContent);
        Assert.Equal("/admin/trips?from=2026-10-01&to=2026-10-08&status=cancelled&pageNumber=1", page.QuerySelector("[data-all-companies]")!.GetAttribute("href"));
        Assert.Equal(operatorId, page.QuerySelector("[data-trip-search-filters] input[name='operatorId']")!.GetAttribute("value"));
        Assert.Equal("BusCo Travels", page.QuerySelector("[data-trip-search-filters] input[name='operatorName']")!.GetAttribute("value"));
    }

    [Theory]
    [InlineData("2026-10-20", "2026-10-10")]
    [InlineData("2026-10-01", "2026-11-01")]
    [InlineData("not-a-date", "2026-10-10")]
    public async Task Get_DateRangeNotAllowed_ShowsRangeMessageWithoutSearching(string from, string to)
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, TwoTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

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
        MapCities(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(Messages.ValidationDateRange, page.QuerySelector("[data-date-range-error]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-trips]"));
    }

    [Fact]
    public async Task Get_FleetRefusesCompany_ShowsWhyAndDefaultSearch()
    {
        // Arrange
        using var host = new FleetTestHost();
        string refusedSearch = "/admin/trips?operatorId=not-an-id&from=2026-10-02&to=2026-10-15&page=1&pageSize=20";
        host.Fleet.MapPathAndQuery(HttpMethod.Get, refusedSearch, () => IdentityStubs.ValidationProblem("operatorId", "The operator must be a UUID."));
        MapFleet(host, TwoTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?operatorId=not-an-id&operatorName=BusCo");

        // Assert
        Assert.Equal("One or more validation errors occurred.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("/admin/trips?from=2026-10-02&to=2026-10-15&page=1&pageSize=20", host.Fleet.Requests.Last().RequestUri!.PathAndQuery);
        Assert.Null(page.QuerySelector("[data-one-company]"));
        Assert.Equal(2, page.QuerySelectorAll("[data-trip]").Length);
    }

    [Fact]
    public async Task Get_NothingFound_ShowsFilteredEmptyMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, Array.Empty<object>());
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("No trips match the current filters.", page.QuerySelector("[data-trips-empty]")!.TextContent);
    }

    [Fact]
    public async Task Get_CitiesNotGiven_SearchesWithoutCityFilters()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => TripStubs.Page(2, 1, TwoTrips()));
        host.Fleet.Map(HttpMethod.Get, CitiesPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Null(page.QuerySelector("#filter-source-city"));
        Assert.Null(page.QuerySelector("#filter-destination-city"));
        Assert.Equal(2, page.QuerySelectorAll("[data-trip]").Length);
        Assert.Null(page.QuerySelector("[data-page-alert]"));
    }

    [Theory]
    [InlineData(false, Messages.ProblemUpstream)]
    [InlineData(true, Messages.ProblemForbidden)]
    public async Task Get_FleetWillNotSearch_ShowsMessageKeepsFilters(bool isForbidden, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => FailedSearch(isForbidden));
        MapCities(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=cancelled");

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("cancelled", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-trips]"));
    }

    [Fact]
    public async Task Get_Operator_ShowsNoPermissionWithoutCallingFleet()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage response = await client.GetAsync(PageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Fleet.Requests);
    }

    private static HttpResponseMessage FailedSearch(bool isForbidden)
    {
        if (isForbidden)
        {
            return IdentityStubs.Problem(HttpStatusCode.Forbidden, "FORBIDDEN");
        }

        throw new HttpRequestException("connection refused");
    }

    private static string TripsRequest(FleetTestHost host)
    {
        HttpRequestMessage request = host.Fleet.Requests.First(candidate => candidate.RequestUri!.AbsolutePath == TripsPath);
        return request.RequestUri!.PathAndQuery;
    }

    private static void MapFleet(FleetTestHost host, object[] trips)
    {
        host.Fleet.Map(HttpMethod.Get, TripsPath, () => TripStubs.Page(trips.Length, 1, trips));
        MapCities(host);
    }

    // Fleet's city list, sent out of order: the page sorts it by name.
    private static void MapCities(FleetTestHost host)
    {
        var pune = new { cityId = PuneId, name = "Pune", state = "Maharashtra" };
        var mumbai = new { cityId = MumbaiId, name = "Mumbai", state = "Maharashtra" };
        host.Fleet.Map(HttpMethod.Get, CitiesPath, () => FakeBackend.Json(new { items = new object[] { pune, mumbai } }));
    }

    /// <summary>Tomorrow night's scheduled trip and a cancelled one two days later.</summary>
    private static object[] TwoTrips()
    {
        return new[] { Trip(_scheduledTripId, "scheduled"), Trip(_cancelledTripId, "cancelled") };
    }

    // Fleet's admin Trip shape: Mumbai → Pune on "Shivneri Sleeper", arriving 3 h 30 min after leaving.
    private static object Trip(Guid tripId, string status)
    {
        bool isCancelled = status == "cancelled";
        var departs = new DateTimeOffset(2026, 10, 3, 17, 0, 0, TimeSpan.Zero);
        if (isCancelled)
        {
            departs = departs.AddDays(1);
        }

        return new
        {
            tripId,
            scheduleId = TripStubs.ScheduleId,
            operatorId = TripStubs.OperatorId.ToString(),
            operatorName = "BusCo Travels",
            routeId = TripStubs.RouteId,
            source = new { cityId = MumbaiId, name = "Mumbai" },
            destination = new { cityId = PuneId, name = "Pune" },
            busId = TripStubs.BusId,
            busName = "Shivneri Sleeper",
            serviceDate = departs.ToOffset(new TimeSpan(5, 30, 0)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            departureAt = departs,
            arrivalAt = departs.AddMinutes(210),
            status,
            classFares = new object[] { new { seatClass = "Lower berth", fare = 1200 }, new { seatClass = "Upper berth", fare = 1000 } },
            cancelReason = isCancelled ? "Bus breakdown at depot" : null,
            cancelledAt = isCancelled ? new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero) : (DateTimeOffset?)null,
        };
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
