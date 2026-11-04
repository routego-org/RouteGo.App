using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Routes;

public sealed class RoutesTests
{
    private const string PageAddress = "/operator/routes";
    private const string FleetPath = "/routes";

    private static readonly Guid _mumbaiPuneId = Guid.Parse("4f1c2b3a-5d6e-4f70-8a9b-0c1d2e3f4a5b");

    [Fact]
    public async Task Get_Operator_ListsRoutesWithDetailsAndLinks()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(2, 1, MumbaiPune("active"), PuneNashik("inactive")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] columns = new[] { "Route", "Stops", "Distance (km)", "Journey time", "Status" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("/routes?page=1&pageSize=20", host.Fleet.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("My routes", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(columns, Texts(page, "[data-routes] th"));
        Assert.Equal(new[] { "Mumbai → Pune via Lonavala", "3", "150", "3 h 30 min", "Active" }, Texts(page, "[data-route]:nth-child(1) td"));
        Assert.Equal(new[] { "Pune → Nashik", "2", "210", "4 h", "Inactive" }, Texts(page, "[data-route]:nth-child(2) td"));
        Assert.Equal($"/operator/routes/{_mumbaiPuneId}", page.QuerySelector("[data-route] a")!.GetAttribute("href"));
        Assert.Equal("Add a route", page.QuerySelector("[data-add-route]")!.TextContent);
        Assert.Equal("/operator/routes/new", page.QuerySelector("[data-add-route]")!.GetAttribute("href"));
        Assert.Equal("Showing 1–2 of 2 routes", page.QuerySelector("[data-routes-showing]")!.TextContent);
        Assert.Equal(new[] { "All", "Active", "Inactive" }, Texts(page, "#filter-status option"));
        Assert.Equal("Show", page.QuerySelector("[data-routes-filters] button")!.TextContent);
        Assert.Null(page.QuerySelector("[data-routes-pages]"));
    }

    [Fact]
    public async Task Get_ActiveFilterSecondPage_SendsFilterAndKeepsItInAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(45, 2, MumbaiPune("active")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=active&pageNumber=2");

        // Assert
        Assert.Equal("/routes?status=active&page=2&pageSize=20", host.Fleet.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("active", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Equal("Showing 21–21 of 45 routes", page.QuerySelector("[data-routes-showing]")!.TextContent);
        Assert.Equal(new[] { "1", "2", "3" }, Texts(page, "[data-routes-pages] a"));
        Assert.Equal("/operator/routes?status=active&pageNumber=3", page.QuerySelectorAll("[data-routes-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_NoRoutesAtAll_ShowsFirstRouteInvitation()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(0, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("You have no routes yet. Add a route to say where your buses go.", page.QuerySelector("[data-routes-empty]")!.TextContent);
        Assert.NotNull(page.QuerySelector("[data-add-route]"));
        Assert.Null(page.QuerySelector("[data-routes]"));
    }

    [Fact]
    public async Task Get_FilterMatchesNothing_ShowsFilteredEmptyMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(0, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=inactive");

        // Assert
        Assert.Equal("No routes match the current filters.", page.QuerySelector("[data-routes-empty]")!.TextContent);
    }

    [Fact]
    public async Task Get_FleetRefusesFilter_ShowsAllRoutesWithProblemTitle()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.MapPathAndQuery(HttpMethod.Get, "/routes?status=retired&page=1&pageSize=20", () => IdentityStubs.ValidationProblem("status", "The status is not valid."));
        host.Fleet.MapPathAndQuery(HttpMethod.Get, "/routes?page=1&pageSize=20", () => Answer(1, 1, MumbaiPune("active")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=retired");

        // Assert
        Assert.Equal("One or more validation errors occurred.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Single(page.QuerySelectorAll("[data-route]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    public async Task Get_FleetWillNotList_ShowsMessageInsteadOfContent(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-routes-filters]"));
        Assert.Null(page.QuerySelector("[data-add-route]"));
        Assert.Null(page.QuerySelector("[data-routes]"));
    }

    [Fact]
    public async Task Get_FleetUnreachable_ShowsUpstreamMessageKeepsFilter()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=active");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("active", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-routes]"));
    }

    [Fact]
    public async Task Get_Customer_ShowsNoPermissionWithoutCallingFleet()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleCustomer);

        // Act
        HttpResponseMessage response = await client.GetAsync(PageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Contains(Messages.AccessGoHome, page.Body!.TextContent);
        Assert.Empty(host.Fleet.Requests);
    }

    private static object MumbaiPune(string status)
    {
        string[] cities = new[] { "Mumbai", "Lonavala", "Pune" };
        return Route(_mumbaiPuneId, cities, 150, 210, status);
    }

    private static object PuneNashik(string status)
    {
        string[] cities = new[] { "Pune", "Nashik" };
        return Route(Guid.NewGuid(), cities, 210, 240, status);
    }

    // Fleet's Route shape; the list reads only part of it.
    private static object Route(Guid routeId, string[] cities, int distanceKm, int journeyMinutes, string status)
    {
        var stops = new List<object>();
        foreach (string city in cities)
        {
            var place = new { cityId = Guid.NewGuid(), name = city, state = "Maharashtra" };
            stops.Add(new { sequence = stops.Count + 1, city = place, offsetMinutes = 0 });
        }

        return new
        {
            routeId,
            operatorId = Guid.NewGuid(),
            stops,
            distanceKm,
            journeyMinutes,
            status,
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            deactivatedAt = (DateTimeOffset?)null,
        };
    }

    private static HttpResponseMessage Answer(int totalCount, int page, params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount, page, pageSize = 20 });
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
