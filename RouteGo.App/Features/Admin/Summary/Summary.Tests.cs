using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.Summary;

public sealed class SummaryTests
{
    private const string Bare = "/admin/summary";
    private const string DefaultAddress = "/admin/summary?from=2026-09-26&to=2026-10-02";

    [Fact]
    public async Task Get_BareAddress_IsSentOnToTheLastSevenDaysAndAsksReportingForThem()
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary);
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);

        // Act
        HttpResponseMessage bare = await client.GetAsync(Bare);
        HttpResponseMessage full = await client.GetAsync(DefaultAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, bare.StatusCode);
        Assert.Equal(DefaultAddress, bare.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Contains(host.Reporting.Requests, request => request.RequestUri!.PathAndQuery == ReportStubs.AdminSummaryQuery("2026-09-26", "2026-10-02"));
        Assert.Equal("Bearer " + host.AccessToken, host.Reporting.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_OneDayAcrossCompanies_ShowsTheFiveTotalsTheDailyRowAndTheNote()
    {
        // Arrange
        object totals = ReportStubs.Totals(5, 11, 13400, 1000);
        object day = ReportStubs.Day("2026-10-02", 5, 11, 13400, 1000);
        using FleetTestHost host = Host(() => ReportStubs.Summary("2026-10-02", "2026-10-02", totals, day));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/summary?from=2026-10-02&to=2026-10-02");

        // Assert
        Assert.Equal("Summary", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("₹13,400", Total(page, "revenue"));
        Assert.Equal("₹1,000", Total(page, "refunds"));
        Assert.Equal("₹12,400", Total(page, "net"));
        Assert.Equal("5", Total(page, "confirmed-bookings"));
        Assert.Equal("11", Total(page, "confirmed-seats"));
        Assert.Equal(new[] { "2 Oct 2026", "5", "11", "₹13,400", "₹1,000", "₹12,400" }, Cells(page, "2026-10-02"));
        Assert.Equal(ReportDisplay.RevenueNote, page.QuerySelector("[data-revenue-note]")!.TextContent);
    }

    [Fact]
    public async Task Get_DayWithNothing_ShowsARowOfZeros()
    {
        // Arrange
        object totals = ReportStubs.Totals(1, 2, 2400, 0);
        object quiet = ReportStubs.Day("2026-10-04", 0, 0, 0, 0);
        using FleetTestHost host = Host(() => ReportStubs.Summary("2026-10-03", "2026-10-05", totals, quiet));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/summary?from=2026-10-03&to=2026-10-05");

        // Assert
        Assert.Equal(new[] { "4 Oct 2026", "0", "0", "₹0", "₹0", "₹0" }, Cells(page, "2026-10-04"));
    }

    [Fact]
    public async Task Get_Summary_OffersTheLinkToSettlements()
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary);
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(DefaultAddress);

        // Assert
        Assert.Equal("/admin/settlements", page.QuerySelector("[data-see-settlements]")!.GetAttribute("href"));
        Assert.Equal("See settlements", page.QuerySelector("[data-see-settlements]")!.TextContent);
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-02")]
    [InlineData("2026-09-01", "2026-10-02")]
    public async Task Get_ReversedOrTooLongRange_ShowsTheRangeMessageKeepsTheDatesAndSendsNothing(string from, string to)
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary);
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync($"/admin/summary?from={from}&to={to}");

        // Assert
        Assert.Equal(Messages.ValidationDateRange, page.QuerySelector("[data-date-range-error]")!.TextContent);
        Assert.Equal(from, page.QuerySelector("#summary-from")!.GetAttribute("value"));
        Assert.Equal(to, page.QuerySelector("#summary-to")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-totals]"));
        Assert.Empty(host.Reporting.Requests);
    }

    [Theory]
    [InlineData("from", "/admin/summary?from=&to=2026-10-02")]
    [InlineData("to", "/admin/summary?from=2026-10-01&to=yesterday")]
    public async Task Get_DayThatIsEmptyOrNotADate_ShowsTheFormatMessageOnThatBoxAndSendsNothing(string key, string address)
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary);
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(address);

        // Assert
        Assert.Equal(Messages.ValidationFormat("date"), page.QuerySelector($"[data-{key}-error]")!.TextContent);
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_ReportingRefusesTheDates_ShowsTheRangeMessage()
    {
        // Arrange
        using FleetTestHost host = Host(() => FakeBackend.ValidationError("to", "The end of the range must be on or after its start."));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(DefaultAddress);

        // Assert
        Assert.Equal(Messages.ValidationDateRange, page.QuerySelector("[data-date-range-error]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-totals]"));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    public async Task Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingReporting(string role)
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary);
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(DefaultAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheSummaryAsReturnAddress()
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary);
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(DefaultAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={DefaultAddress}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_ReportingRefuses_ShowsForbiddenAndNoFigures()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(DefaultAddress);

        // Assert
        Assert.Equal(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-totals]"));
    }

    [Fact]
    public async Task Get_ReportingDown_ShowsUpstreamKeepsTheDatesAndTheMenu()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/summary?from=2026-10-01&to=2026-10-02");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Equal("2026-10-01", page.QuerySelector("#summary-from")!.GetAttribute("value"));
        Assert.Equal("2026-10-02", page.QuerySelector("#summary-to")!.GetAttribute("value"));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    private static HttpResponseMessage EmptySummary()
    {
        return ReportStubs.Summary("2026-09-26", "2026-10-02", ReportStubs.Totals(0, 0, 0, 0));
    }

    private static string Total(IDocument page, string name)
    {
        return page.QuerySelector($"[data-total='{name}']")!.TextContent;
    }

    private static string[] Cells(IDocument page, string day)
    {
        return page.QuerySelectorAll($"[data-day='{day}'] td").Select(cell => cell.TextContent).ToArray();
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Reporting.Map(HttpMethod.Get, ReportStubs.AdminSummaryPath, answer);
        return host;
    }
}
