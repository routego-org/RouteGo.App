using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Dashboard;

public sealed class DashboardAccessTests
{
    private const string Address = "/operator/dashboard?from=2026-10-01&to=2026-10-02&day=2026-10-02";

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public async Task Get_CustomerOrAdmin_ShowsNoPermissionWithoutCallingReporting(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(ReportStubs.DashboardAddress);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheDashboardAsReturnAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(ReportStubs.DashboardAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={ReportStubs.DashboardAddress}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Reporting.Requests);
    }

    [Fact]
    public async Task Get_ReportingRefuses_ShowsForbiddenAndNoFigures()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address);

        // Assert
        Assert.Equal(Messages.ProblemForbidden, page.QuerySelector("[data-sales] [data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-totals]"));
        Assert.Null(page.QuerySelector("[data-trip-list]"));
    }

    [Fact]
    public async Task Get_ReportingDown_ShowsUpstreamInBothSectionsKeepsTheDatesAndTheMenu()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-sales] [data-page-alert]")!.TextContent.Trim());
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-trips] [data-page-alert]")!.TextContent.Trim());
        Assert.Equal("2026-10-01", page.QuerySelector("#sales-from")!.GetAttribute("value"));
        Assert.Equal("2026-10-02", page.QuerySelector("#sales-to")!.GetAttribute("value"));
        Assert.Equal("2026-10-02", page.QuerySelector("#trips-day")!.GetAttribute("value"));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    [Theory]
    [InlineData(30, 40, 75)]
    [InlineData(0, 40, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 67)]
    [InlineData(1, 8, 13)]
    [InlineData(0, 0, 0)]
    public void Percent_SeatsSoldOverSeatsTotal_RoundsToTheNearestWholeNumber(int sold, int total, int expected)
    {
        // Act
        int percent = DashboardHandler.Percent(sold, total);

        // Assert
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(10800, "₹10,800")]
    [InlineData(0, "₹0")]
    [InlineData(-1000, "−₹1,000")]
    [InlineData(1234.4, "₹1,234")]
    [InlineData(-0.2, "₹0")]
    public void Rupees_WholeRupeesWithAMinusSign_ForAnAmount(double amount, string expected)
    {
        // Act
        string text = ReportDisplay.Rupees((decimal)amount);

        // Assert
        Assert.Equal(expected, text);
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Reporting.Map(HttpMethod.Get, ReportStubs.OperatorSummaryPath, answer);
        host.Reporting.Map(HttpMethod.Get, ReportStubs.OperatorTripsPath, answer);
        return host;
    }
}
