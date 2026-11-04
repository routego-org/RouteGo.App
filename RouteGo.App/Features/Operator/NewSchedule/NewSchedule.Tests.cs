using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.NewSchedule;

public sealed class NewScheduleTests
{
    internal const string PageAddress = "/operator/schedules/new";

    [Fact]
    public async Task Get_Operator_OffersActiveRoutesAndBusesAndExplainsSale()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Get, "/routes"));
        Assert.Contains(host.Fleet.Requests, request => request.RequestUri!.PathAndQuery == "/routes?status=active&page=1&pageSize=200");
        Assert.Contains(host.Fleet.Requests, request => request.RequestUri!.PathAndQuery == "/buses?status=active&page=1&pageSize=200");
        Assert.Equal("Add a schedule", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Trips for the next 14 days go on sale as soon as you save.", page.QuerySelector("[data-on-sale-note]")!.TextContent);
        Assert.Equal(new[] { "", "Mumbai → Pune via Lonavala" }, Texts(page, "[data-route] option"));
        Assert.Equal(new[] { "", "Shivneri Sleeper", "Volvo Seater" }, Texts(page, "[data-bus] option"));
        Assert.Equal(days, Texts(page, "[data-days] label"));
        Assert.Equal("Choose a bus to enter its fares.", page.QuerySelector("[data-fares-hint]")!.TextContent);
        Assert.Equal("Show fares for this bus", page.QuerySelector("[data-show-fares]")!.TextContent);
        Assert.Equal("Save schedule", page.QuerySelector("[data-save]")!.TextContent);
        Assert.Equal("/operator/schedules", page.QuerySelector("[data-cancel]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-fare-cutoff]"));
    }

    [Fact]
    public async Task Get_RouteIdFromRoutePage_ChoosesThatRoute()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapChoices(host);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{PageAddress}?routeId={TripStubs.RouteId}");

        // Assert
        Assert.Equal("Mumbai → Pune via Lonavala", page.QuerySelector("[data-route] option[selected]")!.TextContent);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Get_NoActiveRouteOrBus_ShowsWhatIsNeededWithoutForm(bool hasRoute, bool hasBus)
    {
        // Arrange
        using var host = new FleetTestHost();
        object[] routes = ItemsWhen(hasRoute, TripStubs.MumbaiPune("active"));
        object[] buses = ItemsWhen(hasBus, TripStubs.ShivneriSleeper("active"));
        host.Fleet.Map(HttpMethod.Get, "/routes", () => TripStubs.Page(routes.Length, 1, routes));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => TripStubs.Page(buses.Length, 1, buses));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("To add a schedule you need at least one active route and one active bus.", page.QuerySelector("[data-nothing-to-schedule] p")!.TextContent);
        Assert.Equal("/operator/routes/new", page.QuerySelector("[data-add-route]")!.GetAttribute("href"));
        Assert.Equal("/operator/buses/new", page.QuerySelector("[data-add-bus]")!.GetAttribute("href"));
        Assert.Equal("Add a route", page.QuerySelector("[data-add-route]")!.TextContent);
        Assert.Equal("Add a bus", page.QuerySelector("[data-add-bus]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-schedule-form]"));
    }

    [Fact]
    public async Task PostShowFares_BusChosenThenAnother_FareBoxesFollowTheBus()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapChoices(host);
        BusFormDriver driver = await OpenFormAsync(host);
        driver.Set("Input.BusId", TripStubs.BusId.ToString());

        // Act
        await driver.PressAsync("[data-show-fares]");
        string[] firstBus = Texts(driver.Page, "[data-fare-label]");
        driver.Set("Input.Fares[0].Fare", "1200");
        driver.Set("Input.BusId", TripStubs.SecondBusId.ToString());
        await driver.PressAsync("[data-show-fares]");

        // Assert
        Assert.Equal(new[] { "Lower berth (₹)", "Upper berth (₹)" }, firstBus);
        Assert.Equal(new[] { "Seater (₹)" }, Texts(driver.Page, "[data-fare-label]"));
        Assert.Equal("", driver.Page.QuerySelector("[data-fare-box]")!.GetAttribute("value"));
        Assert.Equal(1, host.FleetCalls(HttpMethod.Get, $"/buses/{TripStubs.BusId}"));
        Assert.Equal(1, host.FleetCalls(HttpMethod.Get, $"/buses/{TripStubs.SecondBusId}"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, "/schedules"));
    }

    [Fact]
    public async Task PostShowFares_SeatClassesUnreadable_AsksToChooseBusAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, $"/buses/{TripStubs.BusId}", () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        MapChoices(host);
        BusFormDriver driver = await OpenFormAsync(host);
        driver.Set("Input.BusId", TripStubs.BusId.ToString());

        // Act
        await driver.PressAsync("[data-show-fares]");

        // Assert
        Assert.Equal("We could not load this bus's seat classes. Choose the bus again.", driver.Text("[data-bus-error]"));
        Assert.NotNull(driver.Page.QuerySelector("[data-fares-hint]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "This part of RouteGo is not available right now. Please try again in a few minutes.")]
    public async Task Get_ChoicesRefused_ShowsMessageWithoutForm(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, "/routes", () => IdentityStubs.Problem(status, code));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-schedule-form]"));
        Assert.Null(page.QuerySelector("[data-nothing-to-schedule]"));
    }

    [Fact]
    public async Task Get_ChoiceListRefusedAsInvalid_ShowsInternalError()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, "/routes", () => IdentityStubs.ValidationProblem("pageSize", "Too big."));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => TripStubs.Page(0, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.StartsWith("Something went wrong on our side.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-schedule-form]"));
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

    /// <summary>The operator's active route and its two active buses, each bus answering its seat classes.</summary>
    internal static void MapChoices(FleetTestHost host)
    {
        host.Fleet.Map(HttpMethod.Get, "/routes", () => TripStubs.Page(1, 1, TripStubs.MumbaiPune("active")));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => TripStubs.Page(2, 1, TripStubs.ShivneriSleeper("active"), TripStubs.VolvoSeater("active")));
        host.Fleet.Map(HttpMethod.Get, $"/buses/{TripStubs.BusId}", () => FakeBackend.Json(TripStubs.ShivneriSleeper("active")));
        host.Fleet.Map(HttpMethod.Get, $"/buses/{TripStubs.SecondBusId}", () => FakeBackend.Json(TripStubs.VolvoSeater("active")));
    }

    /// <summary>A signed-in operator's browser on the new-schedule form.</summary>
    internal static async Task<BusFormDriver> OpenFormAsync(FleetTestHost host)
    {
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        return await OpenFormAsync(client);
    }

    /// <summary>The new-schedule form in an already signed-in browser.</summary>
    internal static async Task<BusFormDriver> OpenFormAsync(HttpClient client)
    {
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);
        return new BusFormDriver(client, page);
    }

    // A list page holding the item, or holding nothing.
    private static object[] ItemsWhen(bool isThere, object item)
    {
        if (!isThere)
        {
            return Array.Empty<object>();
        }

        return new[] { item };
    }

    internal static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
