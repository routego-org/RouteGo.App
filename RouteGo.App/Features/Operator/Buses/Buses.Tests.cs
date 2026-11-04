using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Buses;

public sealed class BusesTests
{
    private const string PageAddress = "/operator/buses";
    private const string FleetPath = "/buses";
    private const string CoverPhoto = "https://media.routego.example/buses/alpha/cover.jpg";

    private static readonly Guid _alphaId = Guid.Parse("7a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d");

    [Fact]
    public async Task Get_Operator_ListsBusesWithDetailsAndLinks()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(2, 1, Alpha("active", CoverPhoto), Bravo("inactive")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] columns = new[] { "Cover photo", "Bus name", "Registration number", "Bus type", "AC", "Seats", "Status" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("/buses?page=1&pageSize=20", host.Fleet.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
        Assert.Equal("My buses", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(columns, Texts(page, "[data-buses] th"));
        Assert.Equal(new[] { "", "Alpha", "MH12AB1234", "Sleeper", "Yes", "36", "Active" }, Texts(page, "[data-bus]:nth-child(1) td"));
        Assert.Equal(new[] { "No photo yet", "Bravo", "KA01CD5678", "Seater", "No", "40", "Inactive" }, Texts(page, "[data-bus]:nth-child(2) td"));
        Assert.Equal(CoverPhoto, page.QuerySelector("[data-bus] img")!.GetAttribute("src"));
        Assert.Equal($"/operator/buses/{_alphaId}", page.QuerySelector("[data-bus] a")!.GetAttribute("href"));
        Assert.Equal("Add a bus", page.QuerySelector("[data-add-bus]")!.TextContent);
        Assert.Equal("/operator/buses/new", page.QuerySelector("[data-add-bus]")!.GetAttribute("href"));
        Assert.Equal("Showing 1–2 of 2 buses", page.QuerySelector("[data-buses-showing]")!.TextContent);
        Assert.Equal(new[] { "All", "Active", "Inactive" }, Texts(page, "#filter-status option"));
        Assert.Null(page.QuerySelector("[data-buses-pages]"));
    }

    [Fact]
    public async Task Get_InactiveFilterSecondPage_SendsFilterAndKeepsItInAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(45, 2, Bravo("inactive")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=inactive&pageNumber=2");

        // Assert
        Assert.Equal("/buses?status=inactive&page=2&pageSize=20", host.Fleet.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("inactive", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Single(page.QuerySelectorAll("[data-bus]"));
        Assert.Equal("Showing 21–21 of 45 buses", page.QuerySelector("[data-buses-showing]")!.TextContent);
        Assert.Equal(new[] { "1", "2", "3" }, Texts(page, "[data-buses-pages] a"));
        Assert.Equal("/operator/buses?status=inactive&pageNumber=3", page.QuerySelectorAll("[data-buses-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_CoverPhotoNotAWebAddress_ShowsNoPhoto()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(1, 1, Alpha("active", "javascript:alert(1)")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Null(page.QuerySelector("[data-bus] img"));
        Assert.Equal("No photo yet", page.QuerySelector("[data-cover]")!.TextContent.Trim());
    }

    [Fact]
    public async Task Get_NoBusesAtAll_ShowsFirstBusInvitation()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => Answer(0, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("You have no buses yet. Add your first bus to start selling seats.", page.QuerySelector("[data-buses-empty]")!.TextContent);
        Assert.NotNull(page.QuerySelector("[data-add-bus]"));
        Assert.Null(page.QuerySelector("[data-buses]"));
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
        Assert.Equal("No buses match the current filters.", page.QuerySelector("[data-buses-empty]")!.TextContent);
    }

    [Fact]
    public async Task Get_FleetRefusesFilter_ShowsAllBusesWithProblemTitle()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.MapPathAndQuery(HttpMethod.Get, "/buses?status=retired&page=1&pageSize=20", () => IdentityStubs.ValidationProblem("status", "The status is not valid."));
        host.Fleet.MapPathAndQuery(HttpMethod.Get, "/buses?page=1&pageSize=20", () => Answer(1, 1, Alpha("active", null)));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?status=retired");

        // Assert
        Assert.Equal("One or more validation errors occurred.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Single(page.QuerySelectorAll("[data-bus]"));
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
        Assert.Null(page.QuerySelector("[data-buses-filters]"));
        Assert.Null(page.QuerySelector("[data-add-bus]"));
        Assert.Null(page.QuerySelector("[data-buses]"));
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
        Assert.Null(page.QuerySelector("[data-buses]"));
    }

    [Fact]
    public async Task Get_FleetAnswersUnauthorized_EndsSession()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage response = await client.GetAsync(PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/auth/login?", response.Headers.Location!.OriginalString);
        Assert.Contains("reason=session-ended", response.Headers.Location!.OriginalString);
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
        HttpResponseMessage response = await client.GetAsync(PageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Fleet.Requests);
    }

    private static object Alpha(string status, string? coverPhotoUrl)
    {
        return new
        {
            busId = _alphaId,
            registrationNumber = "MH12AB1234",
            name = "Alpha",
            busType = "sleeper",
            isAc = true,
            totalSeats = 36,
            status,
            coverPhotoUrl,
        };
    }

    private static object Bravo(string status)
    {
        return new
        {
            busId = Guid.NewGuid(),
            registrationNumber = "KA01CD5678",
            name = "Bravo",
            busType = "seater",
            isAc = false,
            totalSeats = 40,
            status,
            coverPhotoUrl = (string?)null,
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
