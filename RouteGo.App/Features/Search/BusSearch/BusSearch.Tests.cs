using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Search.BusSearch;

public sealed class BusSearchTests
{
    private const string FormSelector = "[data-search-form]";

    [Fact]
    public async Task Get_ResultsAddress_ShowsEveryTripWithoutToken()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress);

        // Assert
        Dictionary<string, string> query = SearchStubs.LastQuery(host.Search, SearchStubs.TripsPath);
        Assert.Equal(SearchStubs.Mumbai.ToString(), query["fromCityId"]);
        Assert.Equal(SearchStubs.Pune.ToString(), query["toCityId"]);
        Assert.Equal(SearchStubs.TravelDate, query["date"]);
        Assert.Equal("1", query["page"]);
        Assert.Null(host.Search.LastRequestHeader("Authorization"));
        Assert.Equal("Buses from Mumbai to Pune on 25 Oct 2026", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Mumbai", FieldValue(page, "Search.FromCity"));
        Assert.Equal("Pune", FieldValue(page, "Search.ToCity"));
        Assert.Equal(SearchStubs.TravelDate, FieldValue(page, "Search.Date"));
        Assert.Equal(new[] { SearchStubs.T202.ToString(), SearchStubs.T201.ToString(), SearchStubs.T203.ToString() }, TripOrder(page));
        Assert.Equal("Showing 1–3 of 3 buses.", page.QuerySelector("[data-showing]")!.TextContent);
    }

    [Fact]
    public async Task Get_ResultsAddress_ShowsTripDetailsAndLinks()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress);

        // Assert
        IElement t201 = Trip(page, SearchStubs.T201);
        Assert.Equal("BusCo Volvo 9600", t201.QuerySelector("[data-bus-link]")!.TextContent);
        Assert.Equal($"/buses/{SearchStubs.B11}", t201.QuerySelector("[data-bus-link]")!.GetAttribute("href"));
        Assert.Equal("BusCo Travels", Text(t201, "[data-company]"));
        Assert.Equal("Rating 4.2 out of 5", Text(t201, "[data-rating]"));
        Assert.Equal("Sleeper", Text(t201, "[data-bus-type]"));
        Assert.Equal("Yes", Text(t201, "[data-ac]"));
        Assert.Equal(new[] { "Wi-Fi", "Charging point" }, t201.QuerySelectorAll("[data-amenity]").Select(amenity => amenity.TextContent).ToArray());
        Assert.Equal("10:00 PM IST", Text(t201, "[data-departs]"));
        Assert.Equal("1:00 AM IST, 26 Oct", Text(t201, "[data-arrives]"));
        Assert.Equal("3 h 0 min", Text(t201, "[data-journey]"));
        Assert.Equal("Via Lonavala 11:30 PM IST", Text(t201, "[data-stop]"));
        Assert.Equal("from ₹900", Text(t201, "[data-price]"));
        Assert.Equal("36 seats left", Text(t201, "[data-seats-left]"));
        Assert.Equal($"/trips/{SearchStubs.T201}/seats?route=Mumbai → Pune", Uri.UnescapeDataString(t201.QuerySelector("[data-see-seats]")!.GetAttribute("href")!));
    }

    [Fact]
    public async Task Get_ResultsAddress_ShowsSameDayTripWithoutStopsOrAmenities()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress);

        // Assert
        IElement t202 = Trip(page, SearchStubs.T202);
        Assert.Equal("10:00 AM IST", Text(t202, "[data-arrives]"));
        Assert.Null(t202.QuerySelector("[data-stop]"));
        Assert.Equal("None", Text(t202, "[data-amenities-none]"));
    }

    [Fact]
    public async Task Get_SoldOutTrip_ShowsSoldOutWithPriceAndLinks()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress);

        // Assert
        IElement t203 = Trip(page, SearchStubs.T203);
        Assert.Equal("Sold out", Text(t203, "[data-sold-out]"));
        Assert.Null(t203.QuerySelector("[data-seats-left]"));
        Assert.Equal("from ₹450", Text(t203, "[data-price]"));
        Assert.NotNull(t203.QuerySelector("[data-see-seats]"));
        Assert.Equal($"/buses/{SearchStubs.B13}", t203.QuerySelector("[data-bus-link]")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_SignedIn_ShowsSameResultsWithoutToken(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        HttpClient browser = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await browser.GetAsync(SearchStubs.ResultsAddress);
        IDocument page = await new FormClient(browser).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { SearchStubs.T202.ToString(), SearchStubs.T201.ToString(), SearchStubs.T203.ToString() }, TripOrder(page));
        Assert.Null(host.Search.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Post_FiltersAndSortOnPageTwo_GoesToAddressWithEveryChoiceOnPageOne()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ManyTrips(20, 45, 2)));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&page=2");
        Dictionary<string, string> fields = MumbaiToPune(SearchStubs.TravelDate);
        fields["Search.BusTypes"] = "sleeper";
        fields["Search.IsAc"] = "true";
        fields["Search.Departure"] = "night";
        fields["Search.Sort"] = "lowestFreeFare";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        await visitor.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SearchStubs.ResultsAddress + "&busTypes=sleeper&isAc=true&departure=night&sort=lowestFreeFare", response.Headers.Location!.OriginalString);
        Dictionary<string, string> query = SearchStubs.LastQuery(host.Search, SearchStubs.TripsPath);
        Assert.Equal("sleeper", query["busTypes"]);
        Assert.Equal("true", query["isAc"]);
        Assert.Equal("18:00", query["departAfter"]);
        Assert.Equal("23:59", query["departBefore"]);
        Assert.Equal("lowestFreeFare", query["sort"]);
        Assert.Equal("1", query["page"]);
    }

    [Fact]
    public async Task Get_FilteredAddress_ShowsChoicesInFormAfterReload()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&isAc=true&sort=lowestFreeFare");

        // Assert
        Assert.Equal("true", page.QuerySelector("[data-ac-filter] option[selected]")!.GetAttribute("value"));
        Assert.Equal("lowestFreeFare", page.QuerySelector("[data-sort] option[selected]")!.GetAttribute("value"));
        Assert.Equal(new[] { SearchStubs.T202.ToString(), SearchStubs.T201.ToString(), SearchStubs.T203.ToString() }, TripOrder(page));
    }

    [Fact]
    public async Task Get_PriceUpToAndRating_SendsThemToSearch()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&maxFare=1000&minRating=4.0");

        // Assert
        Dictionary<string, string> query = SearchStubs.LastQuery(host.Search, SearchStubs.TripsPath);
        Assert.Equal("1000", query["maxFare"]);
        Assert.Equal("4.0", query["minRating"]);
        Assert.False(query.ContainsKey("minFare"));
    }

    [Fact]
    public async Task Get_NoBusesWithoutFilter_ShowsTryAnotherDateAndKeepsForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.NoTrips(0)));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        string address = SearchStubs.ResultsAddress.Replace(SearchStubs.TravelDate, "2026-11-30", StringComparison.Ordinal);

        // Act
        IDocument page = await visitor.GetPageAsync(address);

        // Assert
        Assert.Equal("No buses from Mumbai to Pune on 30 Nov 2026. Try another date.", Text(page.DocumentElement, "[data-results-empty-text]"));
        Assert.Null(page.QuerySelector("[data-results-empty-link]"));
        Assert.Equal("Mumbai", FieldValue(page, "Search.FromCity"));
        Assert.Equal("Pune", FieldValue(page, "Search.ToCity"));
        Assert.Equal("2026-11-30", FieldValue(page, "Search.Date"));
    }

    [Fact]
    public async Task Get_FiltersHideEveryBus_ShowsClearFiltersKeepingSort()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.NoTrips(0)));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&busTypes=seater&isAc=true&sort=operatorRating");

        // Assert
        Assert.Equal("No buses match the current filters.", Text(page.DocumentElement, "[data-results-empty-text]"));
        IElement clear = page.QuerySelector("[data-results-empty-link]")!;
        Assert.Equal("Clear filters", clear.TextContent);
        Assert.Equal(SearchStubs.ResultsAddress + "&sort=operatorRating", clear.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_PageTwo_ShowsShowingTextAndPageNavigation()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ManyTrips(20, 45, 2)));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&page=2");

        // Assert
        Assert.Equal("2", SearchStubs.LastQuery(host.Search, SearchStubs.TripsPath)["page"]);
        Assert.Equal("Showing 21–40 of 45 buses.", page.QuerySelector("[data-showing]")!.TextContent);
        IElement[] pages = page.QuerySelectorAll("[data-results-pages] a").ToArray();
        Assert.Equal(new[] { "1", "2", "3" }, pages.Select(link => link.TextContent).ToArray());
        Assert.Equal(SearchStubs.ResultsAddress + "&page=3", pages[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_PagePastTheEnd_ShowsGoToFirstPage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.NoTrips(45)));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&page=9");

        // Assert
        Assert.Equal("There are no buses on this page.", Text(page.DocumentElement, "[data-results-empty-text]"));
        IElement first = page.QuerySelector("[data-results-empty-link]")!;
        Assert.Equal("Go to the first page", first.TextContent);
        Assert.Equal(SearchStubs.ResultsAddress, first.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_PastDateInAddress_ShowsDateMessageWithoutSearching()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        string address = SearchStubs.ResultsAddress.Replace(SearchStubs.TravelDate, "2026-10-01", StringComparison.Ordinal);

        // Act
        IDocument page = await visitor.GetPageAsync(address);

        // Assert
        Assert.Empty(host.Search.Requests);
        Assert.Equal("Choose today or a later date.", Text(page.DocumentElement, "[data-date-error]"));
        Assert.Null(page.QuerySelector("[data-results]"));
        Assert.Equal("Mumbai", FieldValue(page, "Search.FromCity"));
        Assert.Equal(SearchStubs.Mumbai.ToString(), FieldValue(page, "Search.FromCityId"));
        Assert.Equal("Pune", FieldValue(page, "Search.ToCity"));
    }

    [Fact]
    public async Task Get_SearchRefusesSort_ShowsTitleAtTopAndKeepsForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => IdentityStubs.ValidationProblem("Sort", "sort must be departureAt, lowestFreeFare or operatorRating."));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&sort=cheapest");

        // Assert
        Assert.Equal("cheapest", SearchStubs.LastQuery(host.Search, SearchStubs.TripsPath)["sort"]);
        Assert.False(string.IsNullOrWhiteSpace(page.QuerySelector("[data-page-alert]")!.TextContent));
        Assert.Null(page.QuerySelector("[data-results]"));
        Assert.Equal("Mumbai", FieldValue(page, "Search.FromCity"));
        Assert.Equal(SearchStubs.TravelDate, FieldValue(page, "Search.Date"));
    }

    [Fact]
    public async Task Get_AddressWithoutNames_UsesNamesOfFirstResult()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.ThreeTrips()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(AddressWithoutNames());

        // Assert
        Assert.Equal("Buses from Mumbai to Pune on 25 Oct 2026", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Mumbai", FieldValue(page, "Search.FromCity"));
        Assert.Equal("Pune", FieldValue(page, "Search.ToCity"));
    }

    [Fact]
    public async Task Get_AddressWithoutNamesAndNoTrips_ShowsRouteMessageAndEmptyCities()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => FakeBackend.Json(SearchStubs.NoTrips(0)));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(AddressWithoutNames());

        // Assert
        Assert.Equal("No buses for this route on 25 Oct 2026. Try another date.", Text(page.DocumentElement, "[data-results-empty-text]"));
        Assert.Equal("", FieldValue(page, "Search.FromCity"));
        Assert.Equal("", FieldValue(page, "Search.ToCity"));
    }

    [Fact]
    public async Task Get_NoCitiesInAddress_ShowsFormWithTodayWithoutSearching()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync("/bus-search");

        // Assert
        Assert.Equal("Find a bus", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("2026-10-02", FieldValue(page, "Search.Date"));
        Assert.NotNull(page.QuerySelector("[data-search-filters]"));
        Assert.Null(page.QuerySelector("[data-results]"));
        Assert.Empty(host.Search.Requests);
    }

    [Fact]
    public async Task Get_SearchUnreachable_ShowsUpstreamMessageAndKeepsForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.TripsPath, () => throw new HttpRequestException("connection refused"));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync(SearchStubs.ResultsAddress + "&isAc=false");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-results]"));
        Assert.Equal("Mumbai", FieldValue(page, "Search.FromCity"));
        Assert.Equal("false", page.QuerySelector("[data-ac-filter] option[selected]")!.GetAttribute("value"));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    private static Dictionary<string, string> MumbaiToPune(string date)
    {
        return new Dictionary<string, string>
        {
            ["Search.FromCity"] = "Mumbai",
            ["Search.FromCityId"] = SearchStubs.Mumbai.ToString(),
            ["Search.FromCityName"] = "Mumbai",
            ["Search.ToCity"] = "Pune",
            ["Search.ToCityId"] = SearchStubs.Pune.ToString(),
            ["Search.ToCityName"] = "Pune",
            ["Search.Date"] = date,
        };
    }

    private static string AddressWithoutNames()
    {
        return $"/bus-search?fromCityId={SearchStubs.Mumbai}&toCityId={SearchStubs.Pune}&date={SearchStubs.TravelDate}";
    }

    private static string FieldValue(IDocument page, string name)
    {
        return page.QuerySelector($"[name='{name}']")!.GetAttribute("value") ?? "";
    }

    private static string[] TripOrder(IDocument page)
    {
        return page.QuerySelectorAll("[data-trip]").Select(trip => trip.GetAttribute("data-trip")!).ToArray();
    }

    private static IElement Trip(IDocument page, Guid tripId)
    {
        return page.QuerySelector($"[data-trip='{tripId}']")!;
    }

    private static string Text(IElement scope, string selector)
    {
        return scope.QuerySelector(selector)!.TextContent;
    }
}
