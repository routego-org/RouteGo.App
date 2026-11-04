using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.NewRoute;

public sealed class NewRouteTests
{
    [Fact]
    public async Task Get_Operator_ShowsFirstAndLastStopWithSortedCitiesAndWarning()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] cities = new[] { "", "Lonavala, Maharashtra", "Mumbai, Maharashtra", "Nashik, Maharashtra", "Pune, Maharashtra" };

        // Act
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Get, NewRouteTestData.CitiesPath));
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
        Assert.Equal("Add a route", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("You cannot change a route after you create it. Check the stops and times before you save.", page.QuerySelector("[data-route-warning]")!.TextContent);
        Assert.Equal(new[] { "First stop", "Last stop" }, NewRouteTestData.Texts(page, "[data-stop] legend"));
        Assert.Equal(cities, NewRouteTestData.Texts(page, "[data-stop]:first-of-type [data-city] option"));
        Assert.Equal("0 min", page.QuerySelector("[data-first-time]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-stop]:first-of-type [data-hours]"));
        Assert.NotNull(page.QuerySelector("[data-stop]:last-of-type [data-hours]"));
        Assert.NotNull(page.QuerySelector("[data-stop]:last-of-type [data-minutes]"));
        Assert.Empty(page.QuerySelectorAll("[data-remove-stop]"));
        Assert.Equal("Add a stop", page.QuerySelector("[data-add-stop]")!.TextContent);
        Assert.Equal("Distance (km)", page.QuerySelector("label[for=Input_DistanceKm]")!.TextContent);
        Assert.Equal("Create route", page.QuerySelector("[data-create]")!.TextContent);
        Assert.Equal("/operator/routes", page.QuerySelector("[data-cancel]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_EnterKeyInForm_SubmitsCreateNotAStopAction()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Assert
        IElement firstButton = page.QuerySelector(NewRouteTestData.Form + " button")!;
        Assert.Null(firstButton.GetAttribute("formaction"));
        Assert.Equal("Create route", firstButton.TextContent);
    }

    [Fact]
    public async Task Post_RouteWithStopOnTheWay_SendsMinutesAndShowsCreatedRoute()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        host.Fleet.Map(HttpMethod.Post, NewRouteTestData.RoutesPath, () => FakeBackend.Json(NewRouteTestData.Route(), HttpStatusCode.Created));
        host.Fleet.Map(HttpMethod.Get, NewRouteTestData.NewRoutePath, () => FakeBackend.Json(NewRouteTestData.Route()));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);
        string expectedBody = "{\"stops\":[" +
            $"{{\"cityId\":\"{NewRouteTestData.MumbaiId}\",\"offsetMinutes\":0}}," +
            $"{{\"cityId\":\"{NewRouteTestData.LonavalaId}\",\"offsetMinutes\":110}}," +
            $"{{\"cityId\":\"{NewRouteTestData.PuneId}\",\"offsetMinutes\":210}}" +
            "],\"distanceKm\":150}";

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, NewRouteTestData.Form, NewRouteTestData.ValidFields());
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/operator/routes/{NewRouteTestData.NewRouteId}", response.Headers.Location!.OriginalString);
        Assert.Equal(expectedBody, host.Fleet.RequestBodies[0]);
        Assert.Equal("Route created.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Mumbai → Pune via Lonavala", result.QuerySelector("[data-route-name]")!.TextContent);
    }

    [Fact]
    public async Task Post_FleetAnswersUnknownCity_ReloadsCitiesAndKeepsEverythingButCities()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        var cityGone = new Dictionary<string, object> { ["cityId"] = NewRouteTestData.LonavalaId };
        host.Fleet.Map(HttpMethod.Post, NewRouteTestData.RoutesPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "UNKNOWN_CITY", cityGone));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, NewRouteTestData.Form, NewRouteTestData.ValidFields());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "One of the cities is no longer available. We have loaded the city list again. Please choose the cities again.",
            result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(3, host.FleetCalls(HttpMethod.Get, NewRouteTestData.CitiesPath));
        Assert.Empty(result.QuerySelectorAll("[data-city] option[selected]"));
        Assert.Equal(new[] { "1", "3" }, NewRouteTestData.Values(result, "[data-hours]"));
        Assert.Equal(new[] { "50", "30" }, NewRouteTestData.Values(result, "[data-minutes]"));
        Assert.Equal("150", result.QuerySelector("[data-distance]")!.GetAttribute("value"));
    }

    [Theory]
    [InlineData("distanceKm", -1, "[data-distance-error]", "Distance (km) must be between 1 and 3,000.")]
    [InlineData("stops[1].cityId", 1, "[data-city-error]", "City is required.")]
    [InlineData("stops[1].offsetMinutes", -1, "[data-page-alert]", "One or more validation errors occurred.")]
    public async Task Post_FleetRejectsField_ShowsPageWordsAndKeepsValues(string field, int stop, string place, string expected)
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        host.Fleet.Map(HttpMethod.Post, NewRouteTestData.RoutesPath, () => IdentityStubs.ValidationProblem(field, "Fleet's own words."));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, NewRouteTestData.Form, NewRouteTestData.ValidFields());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, NewRouteTestData.MessageAt(result, stop, place));
        Assert.Equal("150", result.QuerySelector("[data-distance]")!.GetAttribute("value"));
        Assert.Equal(3, result.QuerySelectorAll("[data-city] option[selected]").Length);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "This part of RouteGo is not available right now. Please try again in a few minutes.")]
    [InlineData(HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Something went wrong on our side. Try again — if it keeps happening, contact support with reference trace-internal_error.")]
    public async Task Post_FleetRefuses_ShowsMessageAtTopAndKeepsForm(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        host.Fleet.Map(HttpMethod.Post, NewRouteTestData.RoutesPath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, NewRouteTestData.Form, NewRouteTestData.ValidFields());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(3, result.QuerySelectorAll("[data-city] option[selected]").Length);
        Assert.Equal("150", result.QuerySelector("[data-distance]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Post_FleetUnreachableOnCreate_ShowsUpstreamAndKeepsForm()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        host.Fleet.Map(HttpMethod.Post, NewRouteTestData.RoutesPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, NewRouteTestData.Form, NewRouteTestData.ValidFields());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(new[] { "1", "3" }, NewRouteTestData.Values(result, "[data-hours]"));
    }

    [Fact]
    public async Task Get_FleetUnreachable_ShowsUpstreamAndNoForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, NewRouteTestData.CitiesPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector(NewRouteTestData.Form));
    }

    [Fact]
    public async Task Get_FleetAnswersUnauthorized_EndsSession()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, NewRouteTestData.CitiesPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage response = await client.GetAsync(NewRouteTestData.PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("reason=session-ended", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public async Task Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet(string role)
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(NewRouteTestData.PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Fleet.Requests);
    }
}

/// <summary>Fleet's city list and new route, a valid three-stop form, and the reads every NewRoute test shares.</summary>
internal static class NewRouteTestData
{
    public const string PageAddress = "/operator/routes/new";
    public const string CitiesPath = "/cities";
    public const string RoutesPath = "/routes";
    public const string Form = "form[data-new-route-form]";

    public const string MumbaiId = "aaaaaaaa-0000-4000-8000-000000000001";
    public const string LonavalaId = "aaaaaaaa-0000-4000-8000-000000000002";
    public const string PuneId = "aaaaaaaa-0000-4000-8000-000000000003";
    public const string NashikId = "aaaaaaaa-0000-4000-8000-000000000004";

    public static readonly Guid NewRouteId = Guid.Parse("4f1c2b3a-5d6e-4f70-8a9b-0c1d2e3f4a5b");
    public static readonly string NewRoutePath = $"/routes/{NewRouteId}";

    /// <summary>A host whose Fleet answers the city list, deliberately out of name order.</summary>
    public static FleetTestHost Host()
    {
        var host = new FleetTestHost();
        var cities = new[]
        {
            new { cityId = PuneId, name = "Pune", state = "Maharashtra" },
            new { cityId = MumbaiId, name = "Mumbai", state = "Maharashtra" },
            new { cityId = NashikId, name = "Nashik", state = "Maharashtra" },
            new { cityId = LonavalaId, name = "Lonavala", state = "Maharashtra" },
        };
        host.Fleet.Map(HttpMethod.Get, CitiesPath, () => FakeBackend.Json(new { items = cities }));
        return host;
    }

    /// <summary>Mumbai, Lonavala at 1 h 50 min, Pune at 3 h 30 min, 150 km.</summary>
    public static Dictionary<string, string> ValidFields()
    {
        return new Dictionary<string, string>
        {
            ["Input.Stops[0].CityId"] = MumbaiId,
            ["Input.Stops[1].CityId"] = LonavalaId,
            ["Input.Stops[1].Hours"] = "1",
            ["Input.Stops[1].Minutes"] = "50",
            ["Input.Stops[2].CityId"] = PuneId,
            ["Input.Stops[2].Hours"] = "3",
            ["Input.Stops[2].Minutes"] = "30",
            ["Input.DistanceKm"] = "150",
        };
    }

    /// <summary>The route Fleet answers once created: Mumbai → Pune via Lonavala.</summary>
    public static object Route()
    {
        var mumbai = new { cityId = MumbaiId, name = "Mumbai", state = "Maharashtra" };
        var lonavala = new { cityId = LonavalaId, name = "Lonavala", state = "Maharashtra" };
        var pune = new { cityId = PuneId, name = "Pune", state = "Maharashtra" };
        var stops = new[]
        {
            new { sequence = 1, city = mumbai, offsetMinutes = 0 },
            new { sequence = 2, city = lonavala, offsetMinutes = 110 },
            new { sequence = 3, city = pune, offsetMinutes = 210 },
        };
        return new
        {
            routeId = NewRouteId,
            operatorId = Guid.NewGuid(),
            source = mumbai,
            destination = pune,
            stops,
            distanceKm = 150,
            journeyMinutes = 210,
            status = "active",
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            deactivatedAt = (DateTimeOffset?)null,
        };
    }

    public static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    public static string[] Values(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.GetAttribute("value") ?? "").ToArray();
    }

    /// <summary>The message shown in a place: inside the stop at the index, or anywhere on the page when the index is -1.</summary>
    public static string MessageAt(IDocument page, int stop, string place)
    {
        IElement? element = page.QuerySelector(place);
        if (stop >= 0)
        {
            element = page.QuerySelectorAll("[data-stop]")[stop].QuerySelector(place);
        }

        return element!.TextContent.Trim();
    }
}
