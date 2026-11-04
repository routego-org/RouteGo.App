using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.EditSchedule;

public sealed class EditScheduleTests
{
    internal static readonly string PageAddress = $"/operator/schedules/{TripStubs.ScheduleId}/edit";
    internal static readonly string SchedulePath = $"/schedules/{TripStubs.ScheduleId}";

    [Fact]
    public async Task Get_ActiveSchedule_FillsFormWithSavedDetails()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, new ScheduleStub(), "active", "active");
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Contains(host.Fleet.Requests, request => request.RequestUri!.PathAndQuery == "/routes?page=1&pageSize=200");
        Assert.Contains(host.Fleet.Requests, request => request.RequestUri!.PathAndQuery == "/buses?page=1&pageSize=200");
        Assert.Equal("Change Mumbai → Pune via Lonavala, 10:30 PM IST", page.QuerySelector("[data-title]")!.TextContent);
        Assert.Equal(new[] { "", "Mumbai → Pune via Lonavala" }, Texts(page, "[data-route] option"));
        Assert.Equal(TripStubs.RouteId.ToString(), page.QuerySelector("[data-route] option[selected]")!.GetAttribute("value"));
        Assert.Equal(new[] { "", "Shivneri Sleeper" }, Texts(page, "[data-bus] option"));
        Assert.Equal("22:30", page.QuerySelector("[data-departure-time]")!.GetAttribute("value"));
        Assert.Equal(new[] { "monday", "wednesday", "friday" }, page.QuerySelectorAll("[data-days] input[checked]").Select(box => box.GetAttribute("value")!).ToArray());
        Assert.Equal(new[] { "Lower berth (₹)", "Upper berth (₹)" }, Texts(page, "[data-fare-label]"));
        Assert.Equal(new[] { "1200", "1000" }, page.QuerySelectorAll("[data-fare-box]").Select(box => box.GetAttribute("value")!).ToArray());
        Assert.Equal("Trips leaving in less than 6 hours keep their old fares.", page.QuerySelector("[data-fare-cutoff]")!.TextContent);
        Assert.Equal($"/operator/schedules/{TripStubs.ScheduleId}", page.QuerySelector("[data-cancel]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_InactiveSchedule_ShowsStoppedWithLinkAndNoForm()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, new ScheduleStub { Status = "inactive" }, "active", "active");
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.StartsWith(Messages.ProblemScheduleInactive, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Back to the schedule", page.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal($"/operator/schedules/{TripStubs.ScheduleId}", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-schedule-form]"));
    }

    [Fact]
    public async Task Get_ScheduleRouteDeactivated_OffersItMarkedAndChosen()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, new ScheduleStub(), "inactive", "active");
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(new[] { "", "Mumbai → Pune via Lonavala (inactive)" }, Texts(page, "[data-route] option"));
        Assert.Equal(TripStubs.RouteId.ToString(), page.QuerySelector("[data-route] option[selected]")!.GetAttribute("value"));
        Assert.Equal("Change Mumbai → Pune via Lonavala, 10:30 PM IST", page.QuerySelector("[data-title]")!.TextContent);
    }

    [Fact]
    public async Task PostSave_ScheduleBusDeactivated_RefusedByPageWithLinkToBus()
    {
        // Arrange
        using var host = new FleetTestHost();
        MapFleet(host, new ScheduleStub(), "active", "inactive");
        BusFormDriver driver = await OpenFormAsync(host);
        string busChoices = string.Join("|", Texts(driver.Page, "[data-bus] option"));

        // Act
        await driver.PressAsync("[data-save]");

        // Assert
        Assert.Equal("|Shivneri Sleeper (inactive)", busChoices);
        Assert.StartsWith(Messages.ProblemBusInactive, driver.Text("[data-bus-error]"));
        Assert.Equal($"/operator/buses/{TripStubs.BusId}", driver.Page.QuerySelector("[data-open-bus]")!.GetAttribute("href"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Put, SchedulePath));
    }

    [Fact]
    public async Task Get_ScheduleNotFound_ShowsNotFoundWithLinkBack()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulePath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.StartsWith("We couldn't find that schedule. It may have been removed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("/operator/schedules", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-schedule-form]"));
        Assert.Null(page.QuerySelector("[data-title]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    public async Task Get_FleetRefusesSchedule_ShowsMessageWithoutForm(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, SchedulePath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
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

    /// <summary>
    /// The schedule, the operator's routes (its own route in the given status, plus an inactive
    /// Pune → Nashik) and buses (its own bus in the given status, plus an inactive Volvo Seater).
    /// </summary>
    internal static void MapFleet(FleetTestHost host, ScheduleStub schedule, string routeStatus, string busStatus)
    {
        host.Fleet.Map(HttpMethod.Get, SchedulePath, schedule.Answer);
        host.Fleet.Map(HttpMethod.Get, "/routes", () => TripStubs.Page(2, 1, TripStubs.MumbaiPune(routeStatus), TripStubs.PuneNashik("inactive")));
        host.Fleet.Map(HttpMethod.Get, "/buses", () => TripStubs.Page(2, 1, TripStubs.ShivneriSleeper(busStatus), TripStubs.VolvoSeater("inactive")));
        host.Fleet.Map(HttpMethod.Get, $"/buses/{TripStubs.BusId}", () => FakeBackend.Json(TripStubs.ShivneriSleeper(busStatus)));
        host.Fleet.Map(HttpMethod.Get, $"/routes/{TripStubs.RouteId}", () => FakeBackend.Json(TripStubs.MumbaiPune(routeStatus)));
    }

    /// <summary>A signed-in operator's browser on the change form.</summary>
    internal static async Task<BusFormDriver> OpenFormAsync(FleetTestHost host)
    {
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);
        return new BusFormDriver(client, page);
    }

    internal static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
