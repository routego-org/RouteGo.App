using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace RouteGo.App.Features.Operator.NewBus;

public sealed class NewBusTests
{
    private const string PageAddress = "/operator/buses/new";
    private const string FleetPath = "/buses";

    private static readonly Guid _newBusId = Guid.Parse("0b9c8d7e-6f5a-4b3c-9d2e-1f0a9b8c7d6e");

    [Fact]
    public async Task Get_Operator_ShowsEmptyFormWithEveryField()
    {
        // Arrange
        using var host = new FleetTestHost();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        string[] amenities = new[] { "Wi-Fi", "Charging point", "Blanket", "Water bottle", "Reading light", "CCTV", "GPS tracking", "Restroom" };

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("Add a bus", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(new[] { "Seater", "Sleeper", "Semi-sleeper" }, Texts(page, "[data-bus-type] label"));
        Assert.Equal(new[] { "Yes", "No" }, Texts(page, "[data-ac] label"));
        Assert.Equal(amenities, Texts(page, "[data-amenities] label"));
        Assert.Single(page.QuerySelectorAll("[data-seat-class-name]"));
        Assert.Equal("Add a seat class", page.QuerySelector("[data-add-seat-class]")!.TextContent);
        Assert.Equal(new[] { "Lower deck", "Upper deck" }, Texts(page, "[data-deck] h2"));
        Assert.Equal(new[] { "Rows", "Columns", "Aisle after column" }, Texts(page, "[data-deck='lower'] label"));
        Assert.Equal("No aisle", page.QuerySelector("[data-deck='lower'] [data-aisle-after] option")!.TextContent);
        Assert.Equal("This bus has an upper deck", page.QuerySelector("label[for='has-upper-deck']")!.TextContent);
        Assert.Equal("Fill a deck with seats, then remove, rename or change single seats.", page.QuerySelector("[data-layout-hint]")!.TextContent);
        Assert.Equal("Seats: 0 of 60", page.QuerySelector("[data-seat-count]")!.TextContent);
        Assert.Equal("Save bus", page.QuerySelector("[data-save]")!.TextContent);
        Assert.Equal("/operator/buses", page.QuerySelector("[data-cancel]")!.GetAttribute("href"));
        Assert.Empty(host.Fleet.Requests);
    }

    [Fact]
    public async Task PostSave_ValidBus_SendsEveryDetailAndShowsCreatedBus()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => FakeBackend.Json(new { busId = _newBusId }, HttpStatusCode.Created));
        host.Fleet.Map(HttpMethod.Get, $"/buses/{_newBusId}", () => FakeBackend.Json(SavedBus()));
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();

        // Act
        HttpResponseMessage response = await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/operator/buses/{_newBusId}", response.Headers.Location!.OriginalString);
        Assert.Equal(1, host.FleetCalls(HttpMethod.Post, FleetPath));
        JsonElement sent = SentBody(host);
        Assert.Equal("MH-12 AB 1234", sent.GetProperty("registrationNumber").GetString());
        Assert.Equal("Shivneri Sleeper", sent.GetProperty("name").GetString());
        Assert.Equal("sleeper", sent.GetProperty("busType").GetString());
        Assert.True(sent.GetProperty("isAc").GetBoolean());
        Assert.Equal("[\"chargingPoint\",\"blanket\"]", sent.GetProperty("amenities").GetRawText());
        Assert.Equal("[\"Lower berth\",\"Upper berth\"]", sent.GetProperty("seatClasses").GetRawText());
        Assert.Equal(
            "[{\"deck\":\"lower\",\"rows\":6,\"columns\":3,\"aisleAfterColumn\":1},{\"deck\":\"upper\",\"rows\":6,\"columns\":3,\"aisleAfterColumn\":1}]",
            sent.GetProperty("layout").GetProperty("decks").GetRawText());
        Assert.Equal(36, sent.GetProperty("layout").GetProperty("seats").GetArrayLength());
        Assert.Equal(
            "{\"label\":\"1A\",\"deck\":\"lower\",\"row\":1,\"column\":1,\"seatClass\":\"Lower berth\"}",
            sent.GetProperty("layout").GetProperty("seats")[0].GetRawText());
        Assert.Equal(
            "{\"label\":\"U6C\",\"deck\":\"upper\",\"row\":6,\"column\":3,\"seatClass\":\"Upper berth\"}",
            sent.GetProperty("layout").GetProperty("seats")[35].GetRawText());
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task PostSave_ValidBus_FlashesBusCreatedOnBusPage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => FakeBackend.Json(new { busId = _newBusId }, HttpStatusCode.Created));
        host.Fleet.Map(HttpMethod.Get, $"/buses/{_newBusId}", () => FakeBackend.Json(SavedBus()));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        var browser = new FormClient(client);
        var form = new BusFormDriver(client, await browser.GetPageAsync(PageAddress));
        await form.FillShivneriSleeperAsync();

        // Act
        HttpResponseMessage response = await form.PressAsync("[data-save]");
        IDocument busPage = await browser.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal("Bus created.", busPage.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task PostSave_RegistrationTaken_ShowsMessageAtRegistrationAndKeepsForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "BUS_REGISTRATION_ALREADY_EXISTS"));
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();
        form.Set("Input.RegistrationNumber", "mh-12-ab-1234");

        // Act
        HttpResponseMessage response = await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "This registration number is already registered with RouteGo. If this bus is yours, contact RouteGo support.",
            form.Text("[data-registration-error]"));
        Assert.Equal("mh-12-ab-1234", ((IHtmlInputElement)form.Page.QuerySelector("[data-registration]")!).Value);
        Assert.Equal(18, form.SeatLabels("upper").Length);
    }

    [Fact]
    public async Task PostSave_FleetRejectsSeatClass_ShowsMessageAtThatSeat()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => IdentityStubs.ValidationProblem("layout.seats[3].seatClass", "Unknown seat class."));
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal("Enter a valid seat class.", form.Seat("2A").QuerySelector("[data-seat-class-error]")!.TextContent.Trim());
        Assert.Equal(36, form.SeatLabels("lower").Length + form.SeatLabels("upper").Length);
    }

    [Fact]
    public async Task PostSave_FleetRejectsFieldNotOnForm_ShowsTitleAtTop()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => IdentityStubs.ValidationProblem("layout.decks[0].rows", "Rows must be between 1 and 15."));
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal("One or more validation errors occurred.", form.Text("[data-page-alert]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "This part of RouteGo is not available right now. Please try again in a few minutes.")]
    public async Task PostSave_FleetRefuses_ShowsMessageAtTopAndKeepsForm(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => IdentityStubs.Problem(status, code));
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();

        // Act
        HttpResponseMessage response = await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, form.Text("[data-page-alert]"));
        Assert.Equal("Shivneri Sleeper", ((IHtmlInputElement)form.Page.QuerySelector("[data-name]")!).Value);
        Assert.Equal(18, form.SeatLabels("lower").Length);
    }

    [Fact]
    public async Task PostSave_FleetUnreachable_ShowsUpstreamAndKeepsForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => throw new HttpRequestException("connection refused"));
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, form.Text("[data-page-alert]"));
        Assert.Equal(18, form.SeatLabels("upper").Length);
    }

    [Fact]
    public async Task PostSave_FleetAnswersUnauthorized_EndsSession()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, FleetPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();

        // Act
        HttpResponseMessage response = await form.PressAsync("[data-save]");

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
        HttpResponseMessage response = await client.GetAsync(PageAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Fleet.Requests);
    }

    private static async Task<BusFormDriver> OpenAsync(FleetTestHost host)
    {
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);
        return new BusFormDriver(client, page);
    }

    private static JsonElement SentBody(FleetTestHost host)
    {
        using var body = JsonDocument.Parse(host.Fleet.RequestBodies[^1]);
        return body.RootElement.Clone();
    }

    // The bus page reads the new bus back; only its shape matters here.
    private static object SavedBus()
    {
        return new
        {
            busId = _newBusId,
            operatorId = Guid.NewGuid(),
            registrationNumber = "MH12AB1234",
            name = "Shivneri Sleeper",
            busType = "sleeper",
            isAc = true,
            amenities = new[] { "chargingPoint", "blanket" },
            seatClasses = new[] { "Lower berth", "Upper berth" },
            layout = new { decks = Array.Empty<object>(), seats = Array.Empty<object>() },
            totalSeats = 36,
            status = "active",
            photos = Array.Empty<object>(),
            createdAt = new DateTimeOffset(2026, 10, 10, 4, 30, 0, TimeSpan.Zero),
            updatedAt = new DateTimeOffset(2026, 10, 10, 4, 30, 0, TimeSpan.Zero),
        };
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
