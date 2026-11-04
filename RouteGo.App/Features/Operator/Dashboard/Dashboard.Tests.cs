using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Dashboard;

public sealed class DashboardTests
{
    [Fact]
    public async Task Get_BareAddress_IsSentOnToTheLastSevenDaysAndTodayAndAsksReportingForBoth()
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary, () => ReportStubs.Trips());
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage bare = await client.GetAsync(ReportStubs.DashboardAddress);
        HttpResponseMessage full = await client.GetAsync(ReportStubs.DefaultDashboardAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, bare.StatusCode);
        Assert.Equal(ReportStubs.DefaultDashboardAddress, bare.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Contains(host.Reporting.Requests, request => request.RequestUri!.PathAndQuery == ReportStubs.SummaryQuery("2026-09-26", "2026-10-02"));
        Assert.Contains(host.Reporting.Requests, request => request.RequestUri!.PathAndQuery == ReportStubs.TripsQuery("2026-10-02"));
        Assert.Equal("Bearer " + host.AccessToken, host.Reporting.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_TwoDaysOfSales_ShowsTheFiveTotalsTheDailyRowsAndTheNote()
    {
        // Arrange
        object totals = ReportStubs.Totals(4, 9, 10800, 1000);
        object second = ReportStubs.Day("2026-10-02", 3, 7, 8400, 0);
        object third = ReportStubs.Day("2026-10-03", 1, 2, 2400, 1000);
        using FleetTestHost host = Host(() => ReportStubs.Summary("2026-10-02", "2026-10-03", totals, second, third), () => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address("2026-10-02", "2026-10-03", "2026-10-02"));

        // Assert
        Assert.Equal("Dashboard", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("4", Total(page, "confirmed-bookings"));
        Assert.Equal("9", Total(page, "confirmed-seats"));
        Assert.Equal("₹10,800", Total(page, "revenue"));
        Assert.Equal("₹1,000", Total(page, "refunds"));
        Assert.Equal("₹9,800", Total(page, "net"));
        Assert.Equal(new[] { "2 Oct 2026", "3", "7", "₹8,400", "₹0", "₹8,400" }, Cells(page, "2026-10-02"));
        Assert.Equal(new[] { "3 Oct 2026", "1", "2", "₹2,400", "₹1,000", "₹1,400" }, Cells(page, "2026-10-03"));
        Assert.Equal(ReportDisplay.RevenueNote, page.QuerySelector("[data-revenue-note]")!.TextContent);
    }

    [Fact]
    public async Task Get_DayWithNothing_ShowsARowOfZeros()
    {
        // Arrange
        object totals = ReportStubs.Totals(1, 2, 2400, 0);
        object quiet = ReportStubs.Day("2026-10-04", 0, 0, 0, 0);
        using FleetTestHost host = Host(() => ReportStubs.Summary("2026-10-03", "2026-10-05", totals, quiet), () => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address("2026-10-03", "2026-10-05", "2026-10-02"));

        // Assert
        Assert.Equal(new[] { "4 Oct 2026", "0", "0", "₹0", "₹0", "₹0" }, Cells(page, "2026-10-04"));
    }

    [Fact]
    public async Task Get_RefundsOnADayWithNoBookings_ShowsANegativeNetWithAMinusSign()
    {
        // Arrange
        object totals = ReportStubs.Totals(0, 0, 0, 1000);
        object day = ReportStubs.Day("2026-10-02", 0, 0, 0, 1000);
        using FleetTestHost host = Host(() => ReportStubs.Summary("2026-10-02", "2026-10-02", totals, day), () => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address("2026-10-02", "2026-10-02", "2026-10-02"));

        // Assert
        Assert.Equal("−₹1,000", Total(page, "net"));
        Assert.Equal("−₹1,000", Cells(page, "2026-10-02")[5]);
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-02")]
    [InlineData("2026-09-01", "2026-10-02")]
    public async Task Get_ReversedOrTooLongRange_ShowsTheRangeMessageKeepsTheDatesAndDoesNotAskForTheSummary(string from, string to)
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary, () => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address(from, to, "2026-10-02"));

        // Assert
        Assert.Equal(Messages.ValidationDateRange, page.QuerySelector("[data-date-range-error]")!.TextContent);
        Assert.Equal(from, page.QuerySelector("#sales-from")!.GetAttribute("value"));
        Assert.Equal(to, page.QuerySelector("#sales-to")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-totals]"));
        Assert.Equal(0, ReportStubs.Calls(host.Reporting, ReportStubs.OperatorSummaryPath));
        Assert.NotNull(page.QuerySelector("[data-trips]"));
    }

    [Fact]
    public async Task Get_FromThatIsNotADate_ShowsTheFormatMessageOnThatFieldAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary, () => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address("soon", "2026-10-02", "2026-10-02"));

        // Assert
        Assert.Equal("Enter a valid date.", page.QuerySelector("[data-from-error]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-to-error]"));
        Assert.Equal(0, ReportStubs.Calls(host.Reporting, ReportStubs.OperatorSummaryPath));
    }

    [Fact]
    public async Task Get_FromLeftEmpty_IsToldItIsNotADateNotGivenADefault()
    {
        // Arrange
        using FleetTestHost host = Host(EmptySummary, () => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address(string.Empty, "2026-10-02", "2026-10-02"));

        // Assert
        Assert.Equal("Enter a valid date.", page.QuerySelector("[data-from-error]")!.TextContent);
        Assert.Equal(0, ReportStubs.Calls(host.Reporting, ReportStubs.OperatorSummaryPath));
    }

    [Fact]
    public async Task Get_ReportingRefusesTheDates_ShowsTheRangeMessage()
    {
        // Arrange
        using FleetTestHost host = Host(() => FakeBackend.ValidationError("to", "The end of the range must be on or after its start."), () => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(Address("2026-10-01", "2026-10-02", "2026-10-02"));

        // Assert
        Assert.Equal(Messages.ValidationDateRange, page.QuerySelector("[data-date-range-error]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-totals]"));
    }

    private static HttpResponseMessage EmptySummary()
    {
        return ReportStubs.Summary("2026-09-26", "2026-10-02", ReportStubs.Totals(0, 0, 0, 0));
    }

    private static string Address(string from, string to, string day)
    {
        return $"{ReportStubs.DashboardAddress}?from={from}&to={to}&day={day}";
    }

    private static string Total(IDocument page, string name)
    {
        return page.QuerySelector($"[data-total='{name}']")!.TextContent;
    }

    private static string[] Cells(IDocument page, string day)
    {
        IElement row = page.QuerySelector($"[data-day='{day}']")!;
        return row.QuerySelectorAll("td").Select(cell => cell.TextContent).ToArray();
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> summary, Func<HttpResponseMessage> trips)
    {
        var host = new FleetTestHost();
        host.Reporting.Map(HttpMethod.Get, ReportStubs.OperatorSummaryPath, summary);
        host.Reporting.Map(HttpMethod.Get, ReportStubs.OperatorTripsPath, trips);
        return host;
    }
}
