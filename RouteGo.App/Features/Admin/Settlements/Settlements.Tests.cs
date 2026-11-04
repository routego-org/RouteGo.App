using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.Settlements;

public sealed class SettlementsTests
{
    private const string Bare = "/admin/settlements";
    private const string Yesterday = "/admin/settlements?day=2026-10-01";
    private const string ReportLink = "https://reports.example.test/settlements/OP-1.pdf?signature=abc";

    [Fact]
    public async Task Get_BareAddress_IsSentOnToYesterdayAndAsksReportingForIt()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements());
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);

        // Act
        HttpResponseMessage bare = await client.GetAsync(Bare);
        HttpResponseMessage full = await client.GetAsync(Yesterday);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, bare.StatusCode);
        Assert.Equal(Yesterday, bare.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Contains(host.Reporting.Requests, request => request.RequestUri!.PathAndQuery == ReportStubs.SettlementsQuery("2026-10-01"));
        Assert.Equal("Bearer " + host.AccessToken, host.Reporting.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_TwoSettlements_ShowsTheCountAndEveryCompanysFigures()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements(
            ReportStubs.Settlement("2026-10-01", "OP-1", 8400, 1000),
            ReportStubs.Settlement("2026-10-01", "OP-2", 5000, 0)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(Yesterday);

        // Assert
        Assert.Equal("Settlements", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("2 settlements for 1 Oct 2026.", page.QuerySelector("[data-settlements-count]")!.TextContent);
        Assert.Equal(new[] { "OP-1", "₹8,400", "₹1,000", "₹7,400", "30 Oct 2026, 12:05 AM IST", "Download report" }, Cells(page, "OP-1"));
        Assert.Equal("₹5,000", page.QuerySelector("[data-settlement='OP-2'] [data-net]")!.TextContent);
    }

    [Fact]
    public async Task Get_OneSettlement_SaysOneSettlementNotSettlements()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements(ReportStubs.Settlement("2026-10-01", "OP-1", 8400, 1000)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(Yesterday);

        // Assert
        Assert.Equal("1 settlement for 1 Oct 2026.", page.QuerySelector("[data-settlements-count]")!.TextContent);
    }

    [Fact]
    public async Task Get_DayNotSettledYet_SaysTheNightlyRunMayNotHaveFinished()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements());
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/settlements?day=2026-10-02");

        // Assert
        Assert.Equal("No settlements for 2 Oct 2026. The nightly run may not have finished yet.", page.QuerySelector("[data-no-settlements]")!.TextContent);
    }

    [Fact]
    public async Task Get_SettlementWithRefundsOverSales_ShowsANegativeNetWithAMinusSign()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements(ReportStubs.Settlement("2026-10-01", "OP-3", 0, 1000)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(Yesterday);

        // Assert
        Assert.Equal("−₹1,000", page.QuerySelector("[data-settlement='OP-3'] [data-net]")!.TextContent);
    }

    [Theory]
    [InlineData("/admin/settlements?day=")]
    [InlineData("/admin/settlements?day=yesterday")]
    public async Task Get_DayThatIsEmptyOrNotADate_ShowsTheFormatMessageAndSendsNothing(string address)
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements());
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(address);

        // Assert
        Assert.Equal(Messages.ValidationFormat("day"), page.QuerySelector("[data-day-error]")!.TextContent);
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_ReportingRefusesTheDay_ShowsTheFormatMessageAndKeepsTheTypedDay()
    {
        // Arrange
        using FleetTestHost host = Host(() => FakeBackend.ValidationError("date", "A day is required."));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(Yesterday);

        // Assert
        Assert.Equal(Messages.ValidationFormat("day"), page.QuerySelector("[data-day-error]")!.TextContent);
        Assert.Equal("2026-10-01", page.QuerySelector("#settlements-day")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Download_Report_SendsTheBrowserStraightToTheLinkWithoutKeepingItAndAsksReportingAgainEachTime()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements(ReportStubs.Settlement("2026-10-01", "OP-1", 8400, 1000)));
        host.Reporting.MapPathAndQuery(HttpMethod.Get, ReportStubs.DownloadQuery("2026-10-01", "OP-1"), () => ReportStubs.DownloadLink(ReportLink));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);
        IDocument list = await browser.GetPageAsync(Yesterday);
        string address = list.QuerySelector("[data-download]")!.GetAttribute("href")!;
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);

        // Act
        HttpResponseMessage first = await client.GetAsync(address);
        HttpResponseMessage second = await client.GetAsync(address);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(ReportLink, first.Headers.Location!.OriginalString);
        Assert.Equal("no-store", first.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal(2, ReportStubs.Calls(host.Reporting, "/reports/settlements/2026-10-01/download"));
        Assert.DoesNotContain("signature", list.Body!.InnerHtml);
    }

    [Fact]
    public async Task Download_ReportNotThere_ShowsNotAvailableAndLoadsTheSettlementsAgain()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements(ReportStubs.Settlement("2026-10-01", "OP-1", 8400, 1000)));
        host.Reporting.MapPathAndQuery(HttpMethod.Get, ReportStubs.DownloadQuery("2026-10-01", "OP-1"), () => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/settlements?handler=Download&day=2026-10-01&operatorId=OP-1");

        // Assert
        Assert.Equal("This settlement report is not available.", BookingStubs.AlertText(page));
        Assert.NotNull(page.QuerySelector("[data-settlement='OP-1']"));
        Assert.Equal(1, ReportStubs.Calls(host.Reporting, ReportStubs.SettlementsPath));
    }

    [Fact]
    public async Task Download_LinkThatIsNotAWebAddress_IsDroppedAndShowsNotAvailable()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements());
        host.Reporting.MapPathAndQuery(HttpMethod.Get, ReportStubs.DownloadQuery("2026-10-01", "OP-1"), () => ReportStubs.DownloadLink("javascript:alert(1)"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/settlements?handler=Download&day=2026-10-01&operatorId=OP-1");

        // Assert
        Assert.Equal("This settlement report is not available.", BookingStubs.AlertText(page));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    public async Task Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingReporting(string role)
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements());
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(Yesterday);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheSettlementsAsReturnAddress()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Settlements());
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(Yesterday);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={Yesterday}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_ReportingRefuses_ShowsForbidden()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(Yesterday);

        // Assert
        Assert.Equal(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-settlements]"));
    }

    [Fact]
    public async Task Get_ReportingDown_ShowsUpstreamAndKeepsTheTypedDay()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync(Yesterday);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Equal("2026-10-01", page.QuerySelector("#settlements-day")!.GetAttribute("value"));
    }

    private static string[] Cells(IDocument page, string operatorId)
    {
        return page.QuerySelectorAll($"[data-settlement='{operatorId}'] td").Select(cell => cell.TextContent.Trim()).ToArray();
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> list)
    {
        var host = new FleetTestHost();
        host.Reporting.MapPathAndQuery(HttpMethod.Get, ReportStubs.SettlementsQuery("2026-10-01"), list);
        host.Reporting.MapPathAndQuery(HttpMethod.Get, ReportStubs.SettlementsQuery("2026-10-02"), list);
        return host;
    }
}
