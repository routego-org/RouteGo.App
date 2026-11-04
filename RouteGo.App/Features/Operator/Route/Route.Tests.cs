using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Route;

public sealed class RouteTests
{
    private const string ConfirmForm = "form[data-confirm-form]";

    private static readonly Guid _routeId = Guid.Parse("4f1c2b3a-5d6e-4f70-8a9b-0c1d2e3f4a5b");
    private static readonly string _pageAddress = $"/operator/routes/{_routeId}";
    private static readonly string _fleetPath = $"/routes/{_routeId}";
    private static readonly string _deactivatePath = $"/routes/{_routeId}/deactivate";
    private static readonly DateTimeOffset _deactivationMoment = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ActiveRoute_ShowsStopsInOrderWithTimesAndActions()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Route("active", null)));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] stops = new[] { "1. Mumbai, Maharashtra — 0 min", "2. Lonavala, Maharashtra — 1 h 50 min", "3. Pune, Maharashtra — 3 h 30 min" };

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Mumbai → Pune via Lonavala", page.QuerySelector("[data-route-name]")!.TextContent);
        Assert.Equal(stops, Texts(page, "[data-stop]"));
        Assert.Equal("150", page.QuerySelector("[data-distance]")!.TextContent);
        Assert.Equal("3 h 30 min", page.QuerySelector("[data-journey-time]")!.TextContent);
        Assert.Equal("Active", page.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("1 Oct 2026, 10:00 AM IST", page.QuerySelector("[data-created-at]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-deactivated-at]"));
        Assert.Equal(new[] { "Add a schedule on this route", "Deactivate route" }, Texts(page, "[data-route-actions] a"));
        Assert.Equal($"/operator/schedules/new?routeId={_routeId}", page.QuerySelector("[data-route-actions] a")!.GetAttribute("href"));
        Assert.Equal("Back to my routes", page.QuerySelector("[data-back]")!.TextContent);
        Assert.Equal("/operator/routes", page.QuerySelector("[data-back]")!.GetAttribute("href"));
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_Deactivate_AsksFirstAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Route("active", null)));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);
        string confirmAddress = page.QuerySelectorAll("[data-route-actions] a")[1].GetAttribute("href")!;

        // Act
        IDocument confirm = await form.GetPageAsync(confirmAddress);

        // Assert
        Assert.Equal(
            "Deactivate the route Mumbai → Pune via Lonavala? You will not be able to add new schedules on it. Schedules and trips already on it keep running. You cannot turn a route back on.",
            confirm.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Deactivate route", confirm.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal("Cancel", confirm.QuerySelector("[data-cancel]")!.TextContent);
        Assert.Equal(_pageAddress, confirm.QuerySelector("[data-cancel]")!.GetAttribute("href"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, _deactivatePath));
    }

    [Fact]
    public async Task Post_Deactivate_DeactivatesAndShowsInactiveRoute()
    {
        // Arrange
        using var host = new FleetTestHost();
        var route = new SwitchableRoute();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, route.Current);
        host.Fleet.Map(HttpMethod.Post, _deactivatePath, route.Deactivate);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument confirm = await form.GetPageAsync(_pageAddress + "?deactivate=true");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, ConfirmForm, new Dictionary<string, string>());
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(_pageAddress, response.Headers.Location!.OriginalString);
        Assert.Equal(1, host.FleetCalls(HttpMethod.Post, _deactivatePath));
        Assert.Equal("Route deactivated.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Inactive", result.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("8 Oct 2026, 3:30 PM IST", result.QuerySelector("[data-deactivated-at]")!.TextContent);
        Assert.Null(result.QuerySelector("[data-route-actions]"));
    }

    [Fact]
    public async Task Get_InactiveRouteAskedToDeactivate_OffersNoQuestionOrActions()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Route("inactive", _deactivationMoment)));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress + "?deactivate=true");

        // Assert
        Assert.Null(page.QuerySelector("[data-confirm]"));
        Assert.Null(page.QuerySelector("[data-route-actions]"));
        Assert.Equal("Inactive", page.QuerySelector("[data-status]")!.TextContent);
    }

    [Fact]
    public async Task Get_RouteNotFound_ShowsNotFoundWithLinkBack()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith("We couldn't find that route. It may have been removed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Back to my routes", page.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal("/operator/routes", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-stops]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "This part of RouteGo is not available right now. Please try again in a few minutes.")]
    public async Task Get_FleetRefuses_ShowsMessageInsteadOfRoute(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-route-name]"));
        Assert.Null(page.QuerySelector("[data-stops]"));
    }

    [Fact]
    public async Task Get_FleetUnreachable_ShowsUpstreamMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-stops]"));
    }

    [Fact]
    public async Task Post_FleetUnreachable_KeepsRouteAndQuestion()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Route("active", null)));
        host.Fleet.Map(HttpMethod.Post, _deactivatePath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument confirm = await form.GetPageAsync(_pageAddress + "?deactivate=true");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, ConfirmForm, new Dictionary<string, string>());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Messages.ProblemUpstream, result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.NotNull(result.QuerySelector("[data-confirm]"));
        Assert.Equal("Active", result.QuerySelector("[data-status]")!.TextContent);
    }

    [Fact]
    public async Task Post_RouteNotFound_ShowsNotFoundWithoutRoute()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Route("active", null)));
        host.Fleet.Map(HttpMethod.Post, _deactivatePath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument confirm = await form.GetPageAsync(_pageAddress + "?deactivate=true");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, ConfirmForm, new Dictionary<string, string>());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("We couldn't find that route.", result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(result.QuerySelector("[data-stops]"));
        Assert.Null(result.QuerySelector("[data-confirm]"));
    }

    [Fact]
    public async Task Get_FleetAnswersUnauthorized_EndsSession()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage response = await client.GetAsync(_pageAddress);

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
        HttpResponseMessage response = await client.GetAsync(_pageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Fleet.Requests);
    }

    private static object Route(string status, DateTimeOffset? deactivatedAt)
    {
        var mumbai = new { cityId = Guid.NewGuid(), name = "Mumbai", state = "Maharashtra" };
        var lonavala = new { cityId = Guid.NewGuid(), name = "Lonavala", state = "Maharashtra" };
        var pune = new { cityId = Guid.NewGuid(), name = "Pune", state = "Maharashtra" };
        var stops = new[]
        {
            new { sequence = 1, city = mumbai, offsetMinutes = 0 },
            new { sequence = 2, city = lonavala, offsetMinutes = 110 },
            new { sequence = 3, city = pune, offsetMinutes = 210 },
        };
        return new
        {
            routeId = _routeId,
            operatorId = Guid.NewGuid(),
            source = mumbai,
            destination = pune,
            stops,
            distanceKm = 150,
            journeyMinutes = 210,
            status,
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            deactivatedAt,
        };
    }

    private static HttpResponseMessage FleetAnswer(object route)
    {
        return FakeBackend.Json(route);
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    /// <summary>The route Fleet holds: active until the deactivate call, inactive after it.</summary>
    private sealed class SwitchableRoute
    {
        private string _status = "active";
        private DateTimeOffset? _deactivatedAt;

        public HttpResponseMessage Current()
        {
            return FleetAnswer(Route(_status, _deactivatedAt));
        }

        public HttpResponseMessage Deactivate()
        {
            _status = "inactive";
            _deactivatedAt = _deactivationMoment;
            return Current();
        }
    }
}
