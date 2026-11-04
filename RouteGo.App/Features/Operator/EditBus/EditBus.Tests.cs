using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace RouteGo.App.Features.Operator.EditBus;

public sealed class EditBusTests
{
    private static readonly Guid _busId = Guid.Parse("7a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d");
    private static readonly string _pageAddress = $"/operator/buses/{_busId}/edit";
    private static readonly string _fleetPath = $"/buses/{_busId}";

    [Fact]
    public async Task Get_Operator_FillsFormWithSavedBus()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Alpha()));
        BusFormDriver form = await OpenAsync(host);

        // Act
        IDocument page = form.Page;

        // Assert
        Assert.Equal("Change Alpha", page.QuerySelector("[data-title]")!.TextContent);
        Assert.Equal("Alpha", Value(page, "[data-name]"));
        Assert.Equal("MH12AB1234", Value(page, "[data-registration]"));
        Assert.True(IsTicked(page, "input[name='Input.BusType'][value='sleeper']"));
        Assert.True(IsTicked(page, "input[name='Input.IsAc'][value='true']"));
        Assert.True(IsTicked(page, "input[name='Input.Amenities'][value='wifi']"));
        Assert.False(IsTicked(page, "input[name='Input.Amenities'][value='cctv']"));
        Assert.Equal(new[] { "Lower berth", "Upper berth" }, page.QuerySelectorAll("[data-seat-class-name]").Select(box => ((IHtmlInputElement)box).Value).ToArray());
        Assert.True(IsTicked(page, "[data-has-upper-deck]"));
        Assert.Equal(new[] { "L1", "L2", "L3", "L4" }, form.SeatLabels("lower"));
        Assert.Equal(new[] { "U1", "U2" }, form.SeatLabels("upper"));
        Assert.Equal("Upper berth", form.SeatClasses("upper")[0]);
        Assert.Equal("2", ((IHtmlSelectElement)page.QuerySelector("[data-deck='lower'] [data-rows]")!).Value);
        Assert.Equal("1", ((IHtmlSelectElement)page.QuerySelector("[data-deck='lower'] [data-aisle-after]")!).Value);
        Assert.Equal($"/operator/buses/{_busId}", page.QuerySelector("[data-cancel]")!.GetAttribute("href"));
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task PostSave_NameChanged_ReplacesEveryDetailAndShowsUpdatedBus()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Alpha()));
        host.Fleet.Map(HttpMethod.Put, _fleetPath, () => FakeBackend.Json(Alpha()));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        var browser = new FormClient(client);
        var form = new BusFormDriver(client, await browser.GetPageAsync(_pageAddress));
        form.Set("Input.Name", "Alpha Plus");

        // Act
        HttpResponseMessage response = await form.PressAsync("[data-save]");
        IDocument busPage = await browser.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/operator/buses/{_busId}", response.Headers.Location!.OriginalString);
        Assert.Equal(1, host.FleetCalls(HttpMethod.Put, _fleetPath));
        using var sent = JsonDocument.Parse(host.Fleet.RequestBodies[^1]);
        Assert.Equal("Alpha Plus", sent.RootElement.GetProperty("name").GetString());
        Assert.Equal("MH12AB1234", sent.RootElement.GetProperty("registrationNumber").GetString());
        Assert.Equal("[\"wifi\",\"blanket\"]", sent.RootElement.GetProperty("amenities").GetRawText());
        Assert.Equal(2, sent.RootElement.GetProperty("layout").GetProperty("decks").GetArrayLength());
        Assert.Equal(6, sent.RootElement.GetProperty("layout").GetProperty("seats").GetArrayLength());
        Assert.Equal(
            "{\"label\":\"U2\",\"deck\":\"upper\",\"row\":1,\"column\":2,\"seatClass\":\"Upper berth\"}",
            sent.RootElement.GetProperty("layout").GetProperty("seats")[5].GetRawText());
        Assert.Equal("Bus updated.", busPage.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task PostSave_BusHasUpcomingTrips_ShowsTripsAndWhatCanChangeAndKeepsForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Alpha()));
        var extensions = new Dictionary<string, object> { ["futureTripCount"] = 10, ["nextDepartureAt"] = "2026-10-10T16:30:00Z" };
        host.Fleet.Map(HttpMethod.Put, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "BUS_IN_USE", extensions));
        BusFormDriver form = await OpenAsync(host);
        await form.PressAsync("[data-seat='L4'] [data-remove-seat]");

        // Act
        HttpResponseMessage response = await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "This bus has 10 upcoming trips. The next one leaves on 10 Oct 2026, 10:00 PM IST. Stop its schedules or move them to another bus first. You can still change the name, registration number, AC and amenities. To change the bus type, seat classes or seats, the bus must have no upcoming trips.",
            form.Text("[data-page-alert]"));
        Assert.Equal(new[] { "L1", "L2", "L3" }, form.SeatLabels("lower"));
        Assert.Equal("Change Alpha", form.Text("[data-title]"));
    }

    [Fact]
    public async Task PostSave_RegistrationTaken_ShowsMessageAtRegistration()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Alpha()));
        host.Fleet.Map(HttpMethod.Put, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "BUS_REGISTRATION_ALREADY_EXISTS"));
        BusFormDriver form = await OpenAsync(host);
        form.Set("Input.RegistrationNumber", "KA01CD5678");

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal(
            "This registration number is already registered with RouteGo. If this bus is yours, contact RouteGo support.",
            form.Text("[data-registration-error]"));
        Assert.Equal("KA01CD5678", Value(form.Page, "[data-registration]"));
    }

    [Fact]
    public async Task PostBuild_BuilderStep_StaysOnThisBusWithItsTitle()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Alpha()));
        BusFormDriver form = await OpenAsync(host);
        form.Set("Input.Name", "Alpha Plus");

        // Act
        await form.PressAsync("[data-seat='L4'] [data-remove-seat]");

        // Assert
        Assert.Equal("Change Alpha", form.Text("[data-title]"));
        Assert.Equal("Alpha Plus", Value(form.Page, "[data-name]"));
        Assert.Equal($"/operator/buses/{_busId}/edit?handler=Build", form.Page.QuerySelector("[data-fill]")!.GetAttribute("formaction"));
        Assert.Equal(1, host.FleetCalls(HttpMethod.Get, _fleetPath));
    }

    [Fact]
    public async Task Get_BusNotFound_ShowsNotFoundWithLinkBackAndNoForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith("We couldn't find that bus. It may have been removed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("/operator/buses", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-bus-form]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "This part of RouteGo is not available right now. Please try again in a few minutes.")]
    public async Task Get_FleetRefuses_ShowsMessageAndNoForm(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(status, code));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-bus-form]"));
        Assert.Null(page.QuerySelector("[data-title]"));
    }

    [Fact]
    public async Task PostSave_ProfileNotReady_ShowsMessageAndKeepsForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Alpha()));
        host.Fleet.Map(HttpMethod.Put, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY"));
        BusFormDriver form = await OpenAsync(host);
        form.Set("Input.Name", "Alpha Plus");

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal(Messages.ProblemOperatorProfileNotReady, form.Text("[data-page-alert]"));
        Assert.Equal("Alpha Plus", Value(form.Page, "[data-name]"));
    }

    [Fact]
    public async Task PostSave_BusNotFound_ShowsNotFoundAndNoForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Alpha()));
        host.Fleet.Map(HttpMethod.Put, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        BusFormDriver form = await OpenAsync(host);

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.StartsWith("We couldn't find that bus.", form.Text("[data-page-alert]"));
        Assert.Null(form.Page.QuerySelector("[data-bus-form]"));
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
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Customer")]
    public async Task Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(_pageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Fleet.Requests);
    }

    private static async Task<BusFormDriver> OpenAsync(FleetTestHost host)
    {
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(_pageAddress);
        return new BusFormDriver(client, page);
    }

    /// <summary>A small two-deck bus: 2 by 2 lower deck with the aisle after column 1, 1 by 2 upper deck.</summary>
    private static object Alpha()
    {
        object[] decks = new object[]
        {
            new { deck = "lower", rows = 2, columns = 2, aisleAfterColumn = (int?)1 },
            new { deck = "upper", rows = 1, columns = 2, aisleAfterColumn = (int?)null },
        };
        object[] seats = new object[]
        {
            new { label = "L1", deck = "lower", row = 1, column = 1, seatClass = "Lower berth" },
            new { label = "L2", deck = "lower", row = 1, column = 2, seatClass = "Lower berth" },
            new { label = "L3", deck = "lower", row = 2, column = 1, seatClass = "Lower berth" },
            new { label = "L4", deck = "lower", row = 2, column = 2, seatClass = "Lower berth" },
            new { label = "U1", deck = "upper", row = 1, column = 1, seatClass = "Upper berth" },
            new { label = "U2", deck = "upper", row = 1, column = 2, seatClass = "Upper berth" },
        };
        return new
        {
            busId = _busId,
            operatorId = Guid.NewGuid(),
            registrationNumber = "MH12AB1234",
            name = "Alpha",
            busType = "sleeper",
            isAc = true,
            amenities = new[] { "wifi", "blanket" },
            seatClasses = new[] { "Lower berth", "Upper berth" },
            layout = new { decks, seats },
            totalSeats = 6,
            status = "active",
            photos = Array.Empty<object>(),
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            updatedAt = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero),
        };
    }

    private static string Value(IDocument page, string selector)
    {
        return ((IHtmlInputElement)page.QuerySelector(selector)!).Value;
    }

    private static bool IsTicked(IDocument page, string selector)
    {
        return ((IHtmlInputElement)page.QuerySelector(selector)!).IsChecked;
    }
}
