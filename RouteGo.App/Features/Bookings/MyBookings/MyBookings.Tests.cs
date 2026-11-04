using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.MyBookings;

public sealed class MyBookingsTests
{
    private const string PageAddress = "/my-bookings";

    private static readonly Guid _cancelledId = Guid.Parse("60606060-0000-4000-8000-000000000060");
    private static readonly Guid _expiredId = Guid.Parse("55555555-0000-4000-8000-000000000055");

    [Fact]
    public async Task Get_Customer_ListsBookingsInTheOrderBookingGivesWithDetailsRefundsAndLinks()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.ListPage(1, 3, Confirmed(), Cancelled(), Expired()));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("/bookings?page=1&pageSize=20", host.Booking.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
        Assert.Equal("My bookings", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("3 bookings", page.QuerySelector("[data-bookings-showing]")!.TextContent);
        IElement[] items = page.QuerySelectorAll("[data-booking]").ToArray();
        Assert.Equal(new[] { BookingStubs.BookingId.ToString(), _cancelledId.ToString(), _expiredId.ToString() }, items.Select(item => item.GetAttribute("data-booking")!).ToArray());
        Assert.Equal("Mumbai → Pune", items[0].QuerySelector("[data-route]")!.TextContent);
        Assert.Equal("2 Oct 2026, 9:00 PM IST", items[0].QuerySelector("[data-departs]")!.TextContent);
        Assert.Equal("3A, 3B", items[0].QuerySelector("[data-seats]")!.TextContent);
        Assert.Equal("₹1,600", items[0].QuerySelector("[data-total]")!.TextContent);
        Assert.Equal("Confirmed", items[0].QuerySelector("[data-status]")!.TextContent);
        Assert.Null(items[0].QuerySelector("[data-refund]"));
        Assert.Equal("Cancellation · ₹989 · Refund completed", items[1].QuerySelector("[data-refund]")!.TextContent);
        Assert.Equal("Late payment · ₹900 · Refund requested", items[2].QuerySelector("[data-refund]")!.TextContent);
        Assert.Equal("View booking", items[0].QuerySelector("[data-view-booking]")!.TextContent);
        Assert.Equal(BookingStubs.PageAddress, items[0].QuerySelector("[data-view-booking]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-bookings-pages]"));
    }

    [Fact]
    public async Task Get_ThirdOfThreePages_ShowsTheOldestAndPageNumbers()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.ListPage(3, 45, Expired()));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{PageAddress}?page=3");

        // Assert
        Assert.Equal("/bookings?page=3&pageSize=20", host.Booking.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("45 bookings", page.QuerySelector("[data-bookings-showing]")!.TextContent);
        Assert.Equal(new[] { "1", "2", "3" }, page.QuerySelectorAll("[data-bookings-pages] a").Select(link => link.TextContent).ToArray());
        Assert.Equal("/my-bookings", page.QuerySelectorAll("[data-bookings-pages] a")[0].GetAttribute("href"));
        Assert.Equal("/my-bookings?page=2", page.QuerySelectorAll("[data-bookings-pages] a")[1].GetAttribute("href"));
        Assert.Equal("3", page.QuerySelector("[data-bookings-pages] .active")!.TextContent.Trim());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-2")]
    [InlineData("99999999999")]
    public async Task Get_PageThatIsNotAWholeNumberOfOneOrMore_ShowsPageOne(string pageText)
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.ListPage(1, 1, Confirmed()));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{PageAddress}?page={pageText}");

        // Assert
        Assert.Equal("/bookings?page=1&pageSize=20", host.Booking.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("1 booking", page.QuerySelector("[data-bookings-showing]")!.TextContent);
    }

    [Fact]
    public async Task Get_NoBookingsYet_ShowsEmptyMessageAndSearchLink()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.ListPage(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("No bookings yet.", page.QuerySelector("[data-bookings-empty]")!.TextContent);
        Assert.Equal("Search for buses", page.QuerySelector("[data-search-buses]")!.TextContent);
        Assert.Equal("/", page.QuerySelector("[data-search-buses]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Get_BookingDown_ShowsUpstreamMessageAndKeepsTheMenu()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.GatewayTimeout, "UPSTREAM_TIMEOUT"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
        Assert.Null(page.QuerySelector("[data-bookings]"));
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheListAsReturnAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("returnUrl=/my-bookings", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Contains("reason=sign-in-required", response.Headers.Location!.ToString());
        Assert.Empty(host.Booking.Requests);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_OperatorOrAdmin_ShowsNoPermissionWithoutCallingBooking(string role)
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
        Assert.Contains(Messages.AccessGoHome, page.Body!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingsPath, answer);
        return host;
    }

    private static object Confirmed()
    {
        return BookingStubs.ListItem(BookingStubs.BookingId, "confirmed", new[] { "3A", "3B" }, 1600, null);
    }

    private static object Cancelled()
    {
        object refund = BookingStubs.RefundOf("cancellation", 989, "completed");
        return BookingStubs.ListItem(_cancelledId, "cancelled", new[] { "4A" }, 1099, refund);
    }

    private static object Expired()
    {
        object refund = BookingStubs.RefundOf("latePayment", 900, "requested");
        return BookingStubs.ListItem(_expiredId, "expired", new[] { "5C" }, 900, refund);
    }
}
