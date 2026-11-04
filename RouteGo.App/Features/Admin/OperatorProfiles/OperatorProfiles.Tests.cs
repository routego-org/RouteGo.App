using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.OperatorProfiles;

public sealed class OperatorProfilesTests
{
    private const string PageAddress = "/admin/operator-profiles";
    private const string FleetPath = "/admin/operatorProfiles";
    private const string BusCoId = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";
    private const string KonkanId = "5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b";

    [Fact]
    public async Task Get_Admin_ListsEveryCompanyWithDetails()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => TripStubs.Page(2, 1, BusCo(), Konkan()));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        string[] columns = new[] { "Company name", "Email", "Rating", "Approved at", "Operator ID", "" };
        string[] busCo = new[] { "BusCo Travels", "ravi@busco.example", "4.5 out of 5", "1 Oct 2026, 9:30 PM IST", BusCoId, "See trips" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("/admin/operatorProfiles?page=1&pageSize=20", host.Fleet.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal($"Bearer {host.AccessToken}", host.Fleet.Requests.Single().Headers.Authorization!.ToString());
        Assert.Equal("Operators", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(columns, Texts(page, "[data-operator-profiles] th"));
        Assert.Equal(busCo, Texts(page, $"[data-operator-profile='{BusCoId}'] td"));
        Assert.Equal("0.0 out of 5", Texts(page, $"[data-operator-profile='{KonkanId}'] td")[2]);
        Assert.Equal("Showing 1–2 of 2 companies", page.QuerySelector("[data-operator-profiles-showing]")!.TextContent);
        Assert.Equal("A company you just approved can take a minute to appear here.", page.QuerySelector("[data-just-approved]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-operator-profiles-pages]"));
    }

    [Fact]
    public async Task Get_SeeTrips_LinksToTripSearchForThatCompany()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => TripStubs.Page(1, 1, BusCo()));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(
            $"/admin/trips?operatorId={BusCoId}&operatorName=BusCo%20Travels",
            page.QuerySelector($"[data-operator-profile='{BusCoId}'] [data-see-trips]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_SearchByPartOfName_SendsItAndKeepsItInAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => TripStubs.Page(45, 2, BusCo(), Konkan()));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?companyName=busco&pageNumber=2");

        // Assert
        Assert.Equal("/admin/operatorProfiles?companyName=busco&page=2&pageSize=20", host.Fleet.Requests.Single().RequestUri!.PathAndQuery);
        Assert.Equal("busco", page.QuerySelector("#search-company-name")!.GetAttribute("value"));
        Assert.Equal(2, page.QuerySelectorAll("[data-operator-profile]").Length);
        Assert.Equal("Showing 21–22 of 45 companies", page.QuerySelector("[data-operator-profiles-showing]")!.TextContent);
        Assert.Equal(PageAddress + "?companyName=busco&pageNumber=3", page.QuerySelectorAll("[data-operator-profiles-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_SearchMatchesNothing_ShowsSearchEmptyMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => TripStubs.Page(0, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?companyName=zzz");

        // Assert
        Assert.Equal("No companies match your search.", page.QuerySelector("[data-operator-profiles-empty]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-operator-profiles]"));
    }

    [Fact]
    public async Task Get_NoCompanyAtAll_ShowsCollectionEmptyMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => TripStubs.Page(0, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("No approved companies yet.", page.QuerySelector("[data-operator-profiles-empty]")!.TextContent);
    }

    [Fact]
    public async Task Get_SearchLongerThan150Characters_ShowsLengthMessageWithoutCallingFleet()
    {
        // Arrange
        using var host = new FleetTestHost();
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        string companyName = new string('b', 151);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?companyName=" + companyName);

        // Assert
        Assert.Equal("Company name must be 150 characters or fewer.", page.QuerySelector("[data-company-name-error]")!.TextContent);
        Assert.Equal(companyName, page.QuerySelector("#search-company-name")!.GetAttribute("value"));
        Assert.Empty(host.Fleet.Requests);
    }

    [Fact]
    public async Task Get_FleetRefusesCompanyName_ShowsLengthMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => IdentityStubs.ValidationProblem("companyName", "The company name must be 1 to 150 characters."));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?companyName=busco");

        // Assert
        Assert.Equal("Company name must be 150 characters or fewer.", page.QuerySelector("[data-company-name-error]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-operator-profiles]"));
    }

    [Fact]
    public async Task Get_FleetUnreachable_ShowsUpstreamMessageKeepsSearch()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress + "?companyName=busco");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("busco", page.QuerySelector("#search-company-name")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-operator-profiles]"));
    }

    [Fact]
    public async Task Get_FleetForbids_ShowsForbiddenMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, FleetPath, () => IdentityStubs.Problem(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(Messages.ProblemForbidden, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-operator-profiles]"));
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

    // Fleet's OperatorProfile shape: approved 1 Oct 2026 at 4:00 PM UTC (9:30 PM IST).
    private static object BusCo()
    {
        return new
        {
            operatorId = BusCoId,
            companyName = "BusCo Travels",
            email = "ravi@busco.example",
            rating = 4.5m,
            approvedAt = new DateTimeOffset(2026, 10, 1, 16, 0, 0, TimeSpan.Zero),
        };
    }

    private static object Konkan()
    {
        return new
        {
            operatorId = KonkanId,
            companyName = "Konkan Busco",
            email = "ops@konkan.example",
            rating = 0m,
            approvedAt = new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero),
        };
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
