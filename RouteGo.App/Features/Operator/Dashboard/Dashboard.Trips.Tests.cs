using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Dashboard;

public sealed class DashboardTripsTests
{
    private const string FiveOctober = "/operator/dashboard?from=2026-10-01&to=2026-10-02&day=2026-10-05";

    [Fact]
    public async Task Get_TripsOfADay_ShowsRouteDepartureAndSeatsSoldWithPercent()
    {
        // Arrange
        using FleetTestHost host = Host(Trips);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(FiveOctober);

        // Assert
        Assert.Equal("Trips on 5 Oct 2026", page.QuerySelector("[data-trips-heading]")!.TextContent);
        Assert.Equal("2 trips.", page.QuerySelector("[data-trips-count]")!.TextContent);
        Assert.Equal("Mumbai → Pune", Detail(page, "T-1001", "route"));
        Assert.Equal("5 Oct 2026, 10:30 PM IST", Detail(page, "T-1001", "departs"));
        Assert.Equal("30 of 40 seats sold (75%)", Detail(page, "T-1001", "sold"));
        Assert.Equal("0 of 40 seats sold (0%)", Detail(page, "T-1002", "sold"));
        Assert.Contains(host.Reporting.Requests, request => request.RequestUri!.PathAndQuery == ReportStubs.TripsQuery("2026-10-05"));
    }

    [Fact]
    public async Task Get_EveryTrip_LinksToItsSeatsAndPassengersWithTheRouteName()
    {
        // Arrange
        using FleetTestHost host = Host(Trips);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(FiveOctober);

        // Assert
        IElement trip = page.QuerySelector("[data-trip='T-1001']")!;
        Assert.Equal("/operator/trips/T-1001/seats?route=Mumbai%20%E2%86%92%20Pune", trip.QuerySelector("[data-trip-seats]")!.GetAttribute("href"));
        Assert.Equal("/operator/trips/T-1001/passengers?route=Mumbai%20%E2%86%92%20Pune", trip.QuerySelector("[data-trip-passengers]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_OneTrip_SaysOneTripNotTrips()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Trips(OneTrip()));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(FiveOctober);

        // Assert
        Assert.Equal("1 trip.", page.QuerySelector("[data-trips-count]")!.TextContent);
    }

    [Fact]
    public async Task Get_DayWithNoTrips_SaysNoTripsLeaveThatDay()
    {
        // Arrange
        using FleetTestHost host = Host(() => ReportStubs.Trips());
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync("/operator/dashboard?from=2026-10-01&to=2026-10-02&day=2026-10-06");

        // Assert
        Assert.Equal("No trips leave on 6 Oct 2026.", page.QuerySelector("[data-trips-empty]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-trip-list]"));
    }

    [Fact]
    public async Task Get_DayThatIsNotADate_ShowsTheFormatMessageAndDoesNotAskForTrips()
    {
        // Arrange
        using FleetTestHost host = Host(Trips);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync("/operator/dashboard?from=2026-10-01&to=2026-10-02&day=tomorrow");

        // Assert
        Assert.Equal("Enter a valid date.", page.QuerySelector("[data-day-error]")!.TextContent);
        Assert.Equal(0, ReportStubs.Calls(host.Reporting, ReportStubs.OperatorTripsPath));
        Assert.NotNull(page.QuerySelector("[data-totals]"));
    }

    [Fact]
    public async Task Get_TripsSectionOnly_AsksReportingForTripsAndNotTheSummary()
    {
        // Arrange
        using FleetTestHost host = Host(Trips);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument section = await browser.GetPageAsync(FiveOctober + "&handler=Trips");

        // Assert
        Assert.NotNull(section.QuerySelector("[data-trip='T-1001']"));
        Assert.Equal(0, ReportStubs.Calls(host.Reporting, ReportStubs.OperatorSummaryPath));
        Assert.Equal("2026-10-01", section.QuerySelector("[name=from]")!.GetAttribute("value"));
        Assert.Equal("2026-10-02", section.QuerySelector("[name=to]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Get_SalesSectionOnly_AsksReportingForTheSummaryAndNotTrips()
    {
        // Arrange
        using FleetTestHost host = Host(Trips);
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument section = await browser.GetPageAsync(FiveOctober + "&handler=Sales");

        // Assert
        Assert.NotNull(section.QuerySelector("[data-totals]"));
        Assert.Equal(0, ReportStubs.Calls(host.Reporting, ReportStubs.OperatorTripsPath));
        Assert.Equal("2026-10-05", section.QuerySelector("[name=day]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Get_TripsCannotBeAnswered_ShowsTheFiguresAndTheUpstreamMessageInTheTripsSection()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await browser.GetPageAsync(FiveOctober);

        // Assert
        Assert.NotNull(page.QuerySelector("[data-sales] [data-totals]"));
        Assert.Null(page.QuerySelector("[data-sales] [data-page-alert]"));
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-trips] [data-page-alert]")!.TextContent.Trim());
        Assert.Equal("2026-10-05", page.QuerySelector("#trips-day")!.GetAttribute("value"));
    }

    private static HttpResponseMessage Trips()
    {
        return ReportStubs.Trips(OneTrip(), ReportStubs.Trip("T-1002", "Pune", "Mumbai", "2026-10-05T00:30:00Z", 0, 40));
    }

    private static object OneTrip()
    {
        return ReportStubs.Trip("T-1001", "Mumbai", "Pune", "2026-10-05T17:00:00Z", 30, 40);
    }

    private static string Detail(IDocument page, string tripId, string name)
    {
        return page.QuerySelector($"[data-trip='{tripId}'] [data-trip-{name}]")!.TextContent;
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> trips)
    {
        var host = new FleetTestHost();
        host.Reporting.Map(HttpMethod.Get, ReportStubs.OperatorSummaryPath, Summary);
        host.Reporting.Map(HttpMethod.Get, ReportStubs.OperatorTripsPath, trips);
        return host;
    }

    private static HttpResponseMessage Summary()
    {
        return ReportStubs.Summary("2026-10-01", "2026-10-02", ReportStubs.Totals(1, 2, 2400, 0), ReportStubs.Day("2026-10-01", 1, 2, 2400, 0));
    }
}
