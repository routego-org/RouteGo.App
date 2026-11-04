using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Schedule;

public sealed class ScheduleActionsTests
{
    private const string ExtendForm = "form[data-extend-form]";
    private const string ConfirmForm = "form[data-confirm-form]";
    private const string EndDate = "Input.EndDate";

    private static readonly string _pageAddress = $"/operator/schedules/{TripStubs.ScheduleId}";
    private static readonly string _tripsPath = $"/schedules/{TripStubs.ScheduleId}/trips";
    private static readonly string _deactivatePath = $"/schedules/{TripStubs.ScheduleId}/deactivate";
    private static readonly string _activatePath = $"/schedules/{TripStubs.ScheduleId}/activate";

    [Theory]
    [InlineData(7, "7 more trips are now on sale. Trips are on sale until 30 Oct 2026.")]
    [InlineData(0, "No new trips were needed. Trips are on sale until 30 Oct 2026.")]
    public async Task PostExtend_FleetAnswers_ShowsWhatWasPutOnSale(int tripCount, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        host.Fleet.Map(HttpMethod.Post, _tripsPath, () => Extension(tripCount, "2026-10-30"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ExtendForm, Fields("2026-10-30"));
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(_pageAddress, response.Headers.Location!.OriginalString);
        Assert.Equal("{\"endDate\":\"2026-10-30\"}", host.Fleet.LastRequestBody);
        Assert.Equal(expected, result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Theory]
    [InlineData("", "Date is required.")]
    [InlineData("2026-10-01", "Choose a date from today to 1 Dec 2026.")]
    [InlineData("2026-12-02", "Choose a date from today to 1 Dec 2026.")]
    public async Task PostExtend_DateMissingOrOutOfRange_ShowsMessageSendsNothing(string date, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ExtendForm, Fields(date));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, result.QuerySelector("[data-end-date-error]")!.TextContent);
        Assert.Equal(date, result.QuerySelector("[data-end-date]")!.GetAttribute("value"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, _tripsPath));
    }

    [Fact]
    public async Task PostExtend_FleetSaysOutOfRange_ShowsFleetsLatestDate()
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        var latest = new Dictionary<string, object> { ["latestAllowedDate"] = "2026-11-30" };
        host.Fleet.Map(HttpMethod.Post, _tripsPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "TRIP_END_DATE_OUT_OF_RANGE", latest));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ExtendForm, Fields("2026-12-01"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Choose a date from today to 30 Nov 2026.", result.QuerySelector("[data-end-date-error]")!.TextContent);
    }

    [Fact]
    public async Task PostExtend_FleetSaysDateMissing_ShowsRequiredAtDate()
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        host.Fleet.Map(HttpMethod.Post, _tripsPath, () => IdentityStubs.ValidationProblem("endDate", "The endDate field is required."));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ExtendForm, Fields("2026-10-30"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Date is required.", result.QuerySelector("[data-end-date-error]")!.TextContent);
    }

    [Fact]
    public async Task PostExtend_BusTripOverlap_ShowsConflictsInIndiaTime()
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        var conflict = new { serviceDate = "2026-10-26", tripId = Guid.NewGuid(), departureAt = "2026-10-26T16:30:00Z" };
        var conflicts = new Dictionary<string, object> { ["conflicts"] = new[] { conflict } };
        host.Fleet.Map(HttpMethod.Post, _tripsPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "BUS_TRIP_OVERLAP", conflicts));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ExtendForm, Fields("2026-11-06"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(
            "This bus would be on two trips at the same time. A bus needs 60 minutes between arriving and leaving again. It is already busy on: 26 Oct 2026 (a trip leaving 10:00 PM IST). Choose another bus or another time.",
            result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("16 Oct 2026", result.QuerySelector("[data-on-sale-until]")!.TextContent);
    }

    [Fact]
    public async Task PostExtend_BusInactive_ShowsMessageWithLinkToBus()
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        host.Fleet.Map(HttpMethod.Post, _tripsPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "BUS_INACTIVE"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ExtendForm, Fields("2026-11-06"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith(Messages.ProblemBusInactive, result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Open the bus", result.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal($"/operator/buses/{TripStubs.BusId}", result.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task PostExtend_ScheduleStoppedMeanwhile_ShowsMessageAndScheduleAsItNowIs()
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub();
        ScheduleTests.MapSchedule(host, schedule);
        host.Fleet.Map(HttpMethod.Post, _tripsPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "SCHEDULE_INACTIVE"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);
        schedule.Status = "inactive";

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, ExtendForm, Fields("2026-11-06"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("This schedule is stopped. Restart it first.", result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Inactive", result.QuerySelector("[data-status]")!.TextContent);
        Assert.NotNull(result.QuerySelector("[data-restart-form]"));
    }

    [Fact]
    public async Task GetStop_ActiveSchedule_AsksFirstAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);
        string confirmAddress = page.QuerySelector("[data-stop-schedule]")!.GetAttribute("href")!;

        // Act
        IDocument confirm = await form.GetPageAsync(confirmAddress);

        // Assert
        Assert.Equal(_pageAddress + "?stop=true", confirmAddress);
        Assert.Equal(Messages.ConfirmTripsWillBeCancelled, confirm.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Stop schedule", confirm.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal("Go back", confirm.QuerySelector("[data-go-back]")!.TextContent);
        Assert.Equal(_pageAddress, confirm.QuerySelector("[data-go-back]")!.GetAttribute("href"));
        Assert.Null(confirm.QuerySelector("[data-schedule-actions]"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, _deactivatePath));
    }

    [Fact]
    public async Task PostStop_Confirmed_StopsAndOffersRestartOnly()
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub();
        ScheduleTests.MapSchedule(host, schedule);
        host.Fleet.Map(HttpMethod.Post, _deactivatePath, () =>
        {
            schedule.Status = "inactive";
            return FakeBackend.Json(new { schedule = schedule.ToWire(), createdTripCount = 0, cancelledTripCount = 5, repricedTripCount = 0 });
        });
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument confirm = await form.GetPageAsync(_pageAddress + "?stop=true");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, ConfirmForm, new Dictionary<string, string>());
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Schedule stopped. 5 upcoming trips were cancelled.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Inactive", result.QuerySelector("[data-status]")!.TextContent);
        Assert.NotNull(result.QuerySelector("[data-restart-form]"));
        Assert.Null(result.QuerySelector("[data-edit]"));
        Assert.Null(result.QuerySelector("[data-extend]"));
    }

    [Fact]
    public async Task PostStop_FleetUnreachable_KeepsScheduleAndQuestion()
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub());
        host.Fleet.Map(HttpMethod.Post, _deactivatePath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument confirm = await form.GetPageAsync(_pageAddress + "?stop=true");

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
    public async Task PostRestart_InactiveSchedule_RestartsWithoutAsking()
    {
        // Arrange
        using var host = new FleetTestHost();
        var schedule = new ScheduleStub { Status = "inactive" };
        ScheduleTests.MapSchedule(host, schedule);
        host.Fleet.Map(HttpMethod.Post, _activatePath, () =>
        {
            schedule.Status = "active";
            return schedule.Answer();
        });
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, "form[data-restart-form]", new Dictionary<string, string>());
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Post, _activatePath));
        Assert.Equal("Schedule restarted. No trips are on sale yet. Choose a date and put trips on sale.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Active", result.QuerySelector("[data-status]")!.TextContent);
    }

    [Fact]
    public async Task PostRestart_ScheduleNotFound_ShowsNotFoundWithoutSchedule()
    {
        // Arrange
        using var host = new FleetTestHost();
        ScheduleTests.MapSchedule(host, new ScheduleStub { Status = "inactive" });
        host.Fleet.Map(HttpMethod.Post, _activatePath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, "form[data-restart-form]", new Dictionary<string, string>());
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("We couldn't find that schedule.", result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(result.QuerySelector("[data-schedule-fields]"));
    }

    private static Dictionary<string, string> Fields(string endDate)
    {
        return new Dictionary<string, string> { [EndDate] = endDate };
    }

    // Fleet's TripExtension: the trips made (only their number matters here) and the last date on sale.
    private static HttpResponseMessage Extension(int tripCount, string tripsGeneratedThrough)
    {
        var items = new List<object>();
        for (int index = 0; index < tripCount; index++)
        {
            items.Add(new { tripId = Guid.NewGuid() });
        }

        return FakeBackend.Json(new { scheduleId = TripStubs.ScheduleId, tripsGeneratedThrough, items }, HttpStatusCode.Created);
    }
}
