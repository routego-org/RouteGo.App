using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.AuditLog;

public sealed class AuditLogTests
{
    private const string BookingAddress = "/admin/audit-log?kind=Booking&id=B-501";

    [Fact]
    public async Task Get_BookingsHistory_ShowsTheCountAndEachRecordOldestFirstWithWhoWhatAndItem()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-501", 1), () => AuditStubs.Page(1, 2,
            AuditStubs.Record("C-1", "Customer", "BookingCancelled", "Booking", "B-501", "2026-10-02T03:30:00Z"),
            AuditStubs.Record("payment-service", "System", "RefundIssued", "Payment", "P-9", "2026-10-02T03:35:00Z")));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(BookingAddress);

        // Assert
        Assert.Equal("Audit log", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("2 records.", page.QuerySelector("[data-records-count]")!.TextContent);
        Assert.Equal(new[] { "2 Oct 2026, 9:00 AM IST", "2 Oct 2026, 9:05 AM IST" }, Texts(page, "[data-when]"));
        Assert.Equal(new[] { "C-1, Customer", "payment-service, System" }, Texts(page, "[data-who]"));
        Assert.Equal(new[] { "BookingCancelled", "RefundIssued" }, Texts(page, "[data-action]"));
        Assert.Equal(new[] { "Booking B-501", "Payment P-9" }, Texts(page, "[data-item]").Select(Squash).ToArray());
        Assert.Contains(host.Audit.Requests, request => request.RequestUri!.PathAndQuery == AuditStubs.ItemQuery("Booking", "B-501", 1));
        Assert.Equal("Bearer " + host.AccessToken, host.Audit.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_ItemLookup_ShowsDetailsExactlyAsWrittenAndAReadOnlyNote()
    {
        // Arrange
        var details = new Dictionary<string, object> { ["reason"] = "Operator breakdown", ["refundPercent"] = 100 };
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-777", 1), () => AuditStubs.Page(1, 1,
            AuditStubs.Record("A-1", "Admin", "BookingForceCancelled", "Booking", "B-777", "2026-10-01T19:00:00Z", details)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?kind=Booking&id=B-777");

        // Assert
        Assert.Equal(new[] { "reason: Operator breakdown", "refundPercent: 100" }, Texts(page, "[data-detail]"));
        Assert.Equal("1 record.", page.QuerySelector("[data-records-count]")!.TextContent);
        Assert.Contains("Records cannot be changed or removed.", page.QuerySelector("[data-intro]")!.TextContent);
        Assert.Empty(page.QuerySelectorAll("[data-records] button"));
    }

    [Fact]
    public async Task Get_RecordWithoutDetails_ShowsNoDetails()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-501", 1), () => AuditStubs.Page(1, 1,
            AuditStubs.Record("C-1", "Customer", "BookingCancelled", "Booking", "B-501", "2026-10-02T03:30:00Z")));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(BookingAddress);

        // Assert
        Assert.Empty(page.QuerySelectorAll("[data-detail]"));
    }

    [Fact]
    public async Task Get_DayLookup_AsksForThatDayAndShowsItsRecords()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.DayQuery("2026-10-02", 1), () => AuditStubs.Page(1, 1,
            AuditStubs.Record("A-1", "Admin", "BookingForceCancelled", "Booking", "B-777", "2026-10-01T19:00:00Z")));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?day=2026-10-02");

        // Assert
        Assert.Equal(new[] { "2 Oct 2026, 12:30 AM IST" }, Texts(page, "[data-when]"));
        Assert.Equal("2026-10-02", page.QuerySelector("#audit-day")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Get_NoLookup_ShowsOnlyTheFormsAndTheDayBoxOpensOnToday()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.DayQuery("2026-10-02", 1), () => AuditStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log");

        // Assert
        Assert.Equal("2026-10-02", page.QuerySelector("#audit-day")!.GetAttribute("value"));
        Assert.Equal(new[] { "Booking", "Trip", "Payment", "Operator", "Account", "SigningKey" }, Texts(page, "#audit-kind option"));
        Assert.Null(page.QuerySelector("[data-records]"));
        Assert.Empty(host.Audit.Requests);
    }

    [Fact]
    public async Task Get_SecondPage_AsksForThatPageAndKeepsTheLookupInEveryPageLink()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.DayQuery("2026-10-02", 2), () => AuditStubs.Page(2, 120,
            AuditStubs.Record("A-1", "Admin", "BookingForceCancelled", "Booking", "B-777", "2026-10-01T19:00:00Z")));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?day=2026-10-02&page=2");

        // Assert
        Assert.Equal("120 records.", page.QuerySelector("[data-records-count]")!.TextContent);
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, Texts(page, "[data-record-pages] a"));
        Assert.Equal("/admin/audit-log?day=2026-10-02", page.QuerySelector("[data-record-pages] a")!.GetAttribute("href"));
        Assert.Equal("/admin/audit-log?day=2026-10-02&page=6", page.QuerySelectorAll("[data-record-pages] a").Last().GetAttribute("href"));
        Assert.Equal("2", page.QuerySelector("[data-record-pages] .active a")!.TextContent);
    }

    [Fact]
    public async Task Get_NothingFound_SaysNoRecordsFoundAndNoError()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-999", 1), () => AuditStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?kind=Booking&id=B-999");

        // Assert
        Assert.Equal("No records found.", page.QuerySelector("[data-no-records]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-page-alert]"));
    }

    [Fact]
    public async Task Get_ItemWithNoItemId_ShowsRequiredOnTheBoxAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "x", 1), () => AuditStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?kind=Booking&id=");

        // Assert
        Assert.Equal(Messages.ValidationRequired("Item ID"), page.QuerySelector("[data-id-error]")!.TextContent);
        Assert.Empty(host.Audit.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    public async Task Get_DayThatIsEmptyOrNotADate_ShowsTheFormatMessageAndSendsNothing(string day)
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.DayQuery("2026-10-02", 1), () => AuditStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync($"/admin/audit-log?day={day}");

        // Assert
        Assert.Equal(Messages.ValidationFormat("day"), page.QuerySelector("[data-day-error]")!.TextContent);
        Assert.Empty(host.Audit.Requests);
    }

    [Fact]
    public async Task Get_KindThatIsNotOffered_ShowsTheFormatMessageOnKindAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-1", 1), () => AuditStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?kind=booking&id=B-1");

        // Assert
        Assert.Equal(Messages.ValidationFormat("kind"), page.QuerySelector("[data-kind-error]")!.TextContent);
        Assert.Empty(host.Audit.Requests);
    }

    [Fact]
    public async Task Get_RecordsOfABookingAndATrip_LinkTheIdToTheirPagesAndOtherKindsHaveNoLink()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.DayQuery("2026-10-02", 1), () => AuditStubs.Page(1, 3,
            AuditStubs.Record("A-1", "Admin", "BookingCancelled", "Booking", "B-501", "2026-10-02T03:30:00Z"),
            AuditStubs.Record("A-1", "Admin", "TripCancelled", "Trip", "T-9", "2026-10-02T03:31:00Z"),
            AuditStubs.Record("A-1", "Admin", "KeyRotated", "SigningKey", "K-1", "2026-10-02T03:32:00Z")));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?day=2026-10-02");

        // Assert
        IElement[] items = page.QuerySelectorAll("[data-item]").ToArray();
        Assert.Equal("/admin/bookings/B-501", items[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("/admin/trips/T-9/seats", items[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(items[2].QuerySelector("a"));
    }

    [Fact]
    public async Task Get_AuditRefusesTheItem_ShowsTheMessageUnderTheBoxes()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-501", 1), () => FakeBackend.ValidationError("entityId", "Name an entity."));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(BookingAddress);

        // Assert
        Assert.Equal(Messages.ValidationRequired("Item ID"), page.QuerySelector("[data-id-error]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-records]"));
    }

    [Fact]
    public async Task Get_AuditRefusesTheDay_ShowsTheFormatMessageUnderTheDay()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.DayQuery("2026-10-02", 1), () => FakeBackend.ValidationError("date", "Not a day."));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?day=2026-10-02");

        // Assert
        Assert.Equal(Messages.ValidationFormat("day"), page.QuerySelector("[data-day-error]")!.TextContent);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    public async Task Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingAudit(string role)
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-501", 1), () => AuditStubs.Page(1, 0));
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(BookingAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Audit.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheLookupAsReturnAddress()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-501", 1), () => AuditStubs.Page(1, 0));
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(BookingAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={BookingAddress}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Audit.Requests);
    }

    [Fact]
    public async Task Get_AuditForbids_ShowsForbidden()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.ItemQuery("Booking", "B-501", 1), () => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(BookingAddress);

        // Assert
        Assert.Equal(Messages.ProblemForbidden, BookingStubs.AlertText(page));
    }

    [Fact]
    public async Task Get_AuditDown_ShowsUpstreamKeepsTheTypedValuesAndTheMenu()
    {
        // Arrange
        using FleetTestHost host = Host(AuditStubs.DayQuery("2026-10-01", 1), () => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/audit-log?day=2026-10-01");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Equal("2026-10-01", page.QuerySelector("#audit-day")!.GetAttribute("value"));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    private static string Squash(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static FleetTestHost Host(string pathAndQuery, Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Audit.MapPathAndQuery(HttpMethod.Get, pathAndQuery, answer);
        return host;
    }
}
