using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Bus;

public sealed class BusTests
{
    private const string ConfirmForm = "form[data-confirm-form]";
    private const string ActivateForm = "form[data-activate-form]";

    private static readonly Guid _busId = Guid.Parse("7a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d");
    private static readonly string _pageAddress = $"/operator/buses/{_busId}";
    private static readonly string _fleetPath = $"/buses/{_busId}";
    private static readonly string _deactivatePath = $"/buses/{_busId}/deactivate";
    private static readonly string _activatePath = $"/buses/{_busId}/activate";

    [Fact]
    public async Task Get_ActiveBus_ShowsDetailsSeatLayoutAndActions()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("active")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Alpha", page.QuerySelector("[data-bus-name]")!.TextContent);
        Assert.Equal("MH12AB1234", Field(page, "registration-number"));
        Assert.Equal("Sleeper", Field(page, "bus-type"));
        Assert.Equal("Yes", Field(page, "ac"));
        Assert.Equal("Wi-Fi, Charging point, Blanket", Field(page, "amenities"));
        Assert.Equal("Lower berth, Upper berth", Field(page, "seat-classes"));
        Assert.Equal("11", Field(page, "seats"));
        Assert.Equal("Active", Field(page, "status"));
        Assert.Equal("1 Oct 2026, 10:00 AM IST", Field(page, "created-at"));
        Assert.Equal("5 Oct 2026, 3:30 PM IST", Field(page, "updated-at"));
        Assert.Equal(new[] { "Edit bus", "Deactivate bus", "See what customers see" }, Texts(page, "[data-bus-actions] a"));
        Assert.Equal($"/operator/buses/{_busId}/edit", page.QuerySelector("[data-edit]")!.GetAttribute("href"));
        Assert.Equal($"/buses/{_busId}", page.QuerySelector("[data-public]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector(ActivateForm));
        Assert.Equal("/operator/buses", page.QuerySelector("[data-back]")!.GetAttribute("href"));
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_BusWithPhotos_ShowsThemCoverFirstWithLinkToManagePhotos()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => BusPhotoStubs.BusWithPhotos(3, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("2 of 5 photos", page.QuerySelector("[data-photo-count]")!.TextContent);
        string[] photos = page.QuerySelectorAll("[data-photos] [data-photo]").Select(photo => photo.GetAttribute("data-photo")!).ToArray();
        Assert.Equal(new[] { BusPhotoStubs.Photo(3).ToString(), BusPhotoStubs.Photo(1).ToString() }, photos);
        Assert.NotNull(page.QuerySelector($"[data-photo='{BusPhotoStubs.Photo(3)}'] [data-cover]"));
        Assert.Equal(BusPhotoStubs.PhotoUrl(1), page.QuerySelector($"[data-photo='{BusPhotoStubs.Photo(1)}'] img")!.GetAttribute("src"));
        Assert.Equal("Manage photos", page.QuerySelector("[data-manage-photos]")!.TextContent);
        Assert.Equal($"/operator/buses/{_busId}/photos", page.QuerySelector("[data-manage-photos]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_BusWithoutPhotos_SaysNoPhotosYet()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("active")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("No photos yet. Customers trust a bus more when they can see it.", page.QuerySelector("[data-no-photos]")!.TextContent);
        Assert.NotNull(page.QuerySelector("[data-manage-photos]"));
    }

    [Fact]
    public async Task Get_TwoDeckBus_ShowsEachSeatInItsPlaceWithTheAisle()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("active")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(new[] { "Lower deck", "Upper deck" }, Texts(page, "[data-deck-name]"));
        Assert.Equal(new[] { "L1", "", "L2", "L3" }, PlaceTexts(page, "lower", 0));
        Assert.Equal(new[] { "L4", "", "", "L5" }, PlaceTexts(page, "lower", 1));
        Assert.Equal(new[] { "U1", "", "U2", "U3" }, PlaceTexts(page, "upper", 0));
        Assert.NotNull(Row(page, "lower", 0).Children[1].GetAttribute("data-aisle"));
        Assert.NotNull(Row(page, "lower", 1).Children[2].GetAttribute("data-place-empty"));
        Assert.Equal("Lower berth", page.QuerySelector("[data-seat='L1'] .seat-class")!.TextContent);
        Assert.Equal("Upper berth", page.QuerySelector("[data-seat='U1'] .seat-class")!.TextContent);
    }

    [Fact]
    public async Task Get_Deactivate_AsksFirstAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("active")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);
        string confirmAddress = page.QuerySelector("[data-deactivate]")!.GetAttribute("href")!;

        // Act
        IDocument confirm = await form.GetPageAsync(confirmAddress);

        // Assert
        Assert.Equal(
            "Deactivate Alpha? You will not be able to put it on a schedule until you activate it again. Customers can still see its page.",
            confirm.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Deactivate bus", confirm.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal(_pageAddress, confirm.QuerySelector("[data-cancel]")!.GetAttribute("href"));
        Assert.Null(confirm.QuerySelector("[data-bus-actions]"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, _deactivatePath));
    }

    [Fact]
    public async Task Post_Deactivate_DeactivatesAndOffersActivate()
    {
        // Arrange
        using var host = new FleetTestHost();
        var bus = new SwitchableBus("active");
        host.Fleet.Map(HttpMethod.Get, _fleetPath, bus.Current);
        host.Fleet.Map(HttpMethod.Post, _deactivatePath, bus.Deactivate);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument confirm = await form.GetPageAsync(_pageAddress + "?deactivate=true");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, ConfirmForm, new Dictionary<string, string>());
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(_pageAddress, response.Headers.Location!.OriginalString);
        Assert.Equal(1, host.FleetCalls(HttpMethod.Post, _deactivatePath));
        Assert.Equal("Bus deactivated.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Inactive", Field(result, "status"));
        Assert.Equal("Activate bus", result.QuerySelector(ActivateForm + " button")!.TextContent);
        Assert.Null(result.QuerySelector("[data-deactivate]"));
    }

    [Fact]
    public async Task Post_DeactivateBusWithUpcomingTrips_ShowsTripsAndKeepsBusActive()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("active")));
        var extensions = new Dictionary<string, object> { ["futureTripCount"] = 3, ["nextDepartureAt"] = "2026-10-10T16:30:00Z" };
        host.Fleet.Map(HttpMethod.Post, _deactivatePath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "BUS_IN_USE", extensions));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument confirm = await form.GetPageAsync(_pageAddress + "?deactivate=true");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, ConfirmForm, new Dictionary<string, string>());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith(
            "This bus has 3 upcoming trips. The next one leaves on 10 Oct 2026, 10:00 PM IST. Stop its schedules or move them to another bus first.",
            result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("See this bus's schedules", result.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal($"/operator/schedules?busId={_busId}", result.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal("Active", Field(result, "status"));
        Assert.Null(result.QuerySelector("[data-confirm]"));
    }

    [Fact]
    public async Task Post_Activate_ActivatesWithoutAskingAndOffersDeactivate()
    {
        // Arrange
        using var host = new FleetTestHost();
        var bus = new SwitchableBus("inactive");
        host.Fleet.Map(HttpMethod.Get, _fleetPath, bus.Current);
        host.Fleet.Map(HttpMethod.Post, _activatePath, bus.Activate);
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ActivateForm, new Dictionary<string, string>());
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(_pageAddress, response.Headers.Location!.OriginalString);
        Assert.Equal(1, host.FleetCalls(HttpMethod.Post, _activatePath));
        Assert.Equal("Bus activated.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Active", Field(result, "status"));
        Assert.NotNull(result.QuerySelector("[data-deactivate]"));
        Assert.Null(result.QuerySelector(ActivateForm));
    }

    [Fact]
    public async Task Get_InactiveBusAskedToDeactivate_OffersActivateInsteadOfQuestion()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("inactive")));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress + "?deactivate=true");

        // Assert
        Assert.Null(page.QuerySelector("[data-confirm]"));
        Assert.Null(page.QuerySelector("[data-deactivate]"));
        Assert.NotNull(page.QuerySelector(ActivateForm));
    }

    [Fact]
    public async Task Get_BusNotFound_ShowsNotFoundWithLinkBack()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.StartsWith("We couldn't find that bus. It may have been removed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Back to my buses", page.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal("/operator/buses", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-bus-fields]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "This part of RouteGo is not available right now. Please try again in a few minutes.")]
    public async Task Get_FleetRefuses_ShowsMessageInsteadOfBus(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-bus-name]"));
        Assert.Null(page.QuerySelector("[data-bus-fields]"));
        Assert.Null(page.QuerySelector("[data-bus-actions]"));
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
        Assert.Null(page.QuerySelector("[data-bus-fields]"));
    }

    [Fact]
    public async Task Post_DeactivateFleetUnreachable_KeepsBusAndQuestion()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("active")));
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
        Assert.Equal("Active", Field(result, "status"));
    }

    [Fact]
    public async Task Post_ActivateBusNotFound_ShowsNotFoundWithoutBus()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FleetAnswer(Bus("inactive")));
        host.Fleet.Map(HttpMethod.Post, _activatePath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ActivateForm, new Dictionary<string, string>());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("We couldn't find that bus.", result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(result.QuerySelector("[data-bus-fields]"));
        Assert.Null(result.QuerySelector(ActivateForm));
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

    /// <summary>
    /// A two-deck sleeper, 2 rows by 3 columns per deck with the aisle after column 1; the lower
    /// deck's second row has an empty place in column 2.
    /// </summary>
    private static object Bus(string status)
    {
        var decks = new[]
        {
            new { deck = "lower", rows = 2, columns = 3, aisleAfterColumn = 1 },
            new { deck = "upper", rows = 2, columns = 3, aisleAfterColumn = 1 },
        };
        object[] seats = new[]
        {
            Seat("L1", "lower", 1, 1), Seat("L2", "lower", 1, 2), Seat("L3", "lower", 1, 3),
            Seat("L4", "lower", 2, 1), Seat("L5", "lower", 2, 3),
            Seat("U1", "upper", 1, 1), Seat("U2", "upper", 1, 2), Seat("U3", "upper", 1, 3),
            Seat("U4", "upper", 2, 1), Seat("U5", "upper", 2, 2), Seat("U6", "upper", 2, 3),
        };
        return new
        {
            busId = _busId,
            operatorId = Guid.NewGuid(),
            registrationNumber = "MH12AB1234",
            name = "Alpha",
            busType = "sleeper",
            isAc = true,
            amenities = new[] { "wifi", "chargingPoint", "blanket" },
            seatClasses = new[] { "Lower berth", "Upper berth" },
            layout = new { decks, seats },
            totalSeats = 11,
            status,
            photos = Array.Empty<object>(),
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            updatedAt = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero),
        };
    }

    private static object Seat(string label, string deck, int row, int column)
    {
        string seatClass = deck == "upper" ? "Upper berth" : "Lower berth";
        return new { label, deck, row, column, seatClass };
    }

    private static HttpResponseMessage FleetAnswer(object bus)
    {
        return FakeBackend.Json(bus);
    }

    private static string Field(IDocument page, string name)
    {
        return page.QuerySelector($"[data-field='{name}']")!.TextContent.Trim();
    }

    private static IElement Row(IDocument page, string deck, int index)
    {
        return page.QuerySelectorAll($"[data-deck='{deck}'] [data-seat-row]")[index];
    }

    private static string[] PlaceTexts(IDocument page, string deck, int index)
    {
        return Row(page, deck, index).Children.Select(cell => Number(cell)).ToArray();
    }

    private static string Number(IElement cell)
    {
        IElement? number = cell.QuerySelector(".seat-number");
        if (number is null)
        {
            return string.Empty;
        }

        return number.TextContent;
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    /// <summary>The bus Fleet holds, whose status the activate and deactivate calls switch.</summary>
    private sealed class SwitchableBus
    {
        private string _status;

        public SwitchableBus(string status)
        {
            _status = status;
        }

        public HttpResponseMessage Current()
        {
            return FleetAnswer(Bus(_status));
        }

        public HttpResponseMessage Deactivate()
        {
            _status = "inactive";
            return Current();
        }

        public HttpResponseMessage Activate()
        {
            _status = "active";
            return Current();
        }
    }
}
