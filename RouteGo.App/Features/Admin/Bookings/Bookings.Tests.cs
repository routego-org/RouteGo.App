using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.Bookings;

public sealed class BookingsTests
{
    private static readonly Guid _confirmedId = BookingStubs.BookingId;
    private static readonly Guid _failedId = AdminBookingStubs.FailedBookingId;

    [Fact]
    public async Task Get_Email_SendsItAsTypedAndListsNewestFirstWithEveryColumn()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 2,
            AdminBookingStubs.Item(_failedId, "failed", new[] { "3A" }, 800),
            AdminBookingStubs.Item(_confirmedId, "confirmed", new[] { "3A", "3B" }, 1600)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings?email=Priya%40Example.com");

        // Assert
        Assert.Equal($"{{\"bookingId\":null,\"contactEmail\":\"Priya@Example.com\",\"tripId\":null}}", host.Booking.LastRequestBody);
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
        Assert.Equal(new[] { _failedId.ToString(), _confirmedId.ToString() }, Texts(page, "[data-open-booking]"));
        Assert.Equal("2 bookings.", page.QuerySelector("[data-bookings-count]")!.TextContent);
        Assert.Equal(new[] { _failedId.ToString(), "Failed", "Priya Shah", "priya@example.com", "Mumbai → Pune, 2 Oct 2026, 9:00 PM IST", "3A", "₹800", "1 Oct 2026, 10:30 AM IST" }, Cells(page, _failedId));
        Assert.Equal("3A, 3B", page.QuerySelector($"[data-booking='{_confirmedId}'] [data-seats]")!.TextContent);
        Assert.Equal("Priya@Example.com", page.QuerySelector("#search-email")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Get_Trip_ListsEveryStatusWaitingForPaymentIncludedAndSaysHowManyBookings()
    {
        // Arrange
        var waiting = Guid.NewGuid();
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 16,
            AdminBookingStubs.Item(waiting, "paymentPending", new[] { "4A" }, 700),
            AdminBookingStubs.Item(_confirmedId, "confirmed", new[] { "3A" }, 800)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync($"/admin/bookings?tripId={BookingStubs.TripId}");

        // Assert
        Assert.Equal("16 bookings.", page.QuerySelector("[data-bookings-count]")!.TextContent);
        Assert.Equal("Waiting for payment", page.QuerySelector($"[data-booking='{waiting}'] [data-status]")!.TextContent);
        Assert.Contains($"\"tripId\":\"{BookingStubs.TripId}\"", host.Booking.LastRequestBody);
        Assert.Contains("\"bookingId\":null", host.Booking.LastRequestBody);
    }

    [Fact]
    public async Task Get_OneBooking_SaysOneBookingNotBookings()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 1, AdminBookingStubs.Item(_confirmedId, "confirmed", new[] { "3A" }, 800)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync($"/admin/bookings?bookingId={_confirmedId}");

        // Assert
        Assert.Equal("1 booking.", page.QuerySelector("[data-bookings-count]")!.TextContent);
    }

    [Fact]
    public async Task Get_SecondPage_AsksForItAndEveryPageLinkKeepsTheTripAndThePage()
    {
        // Arrange
        using FleetTestHost host = Host(2, () => AdminBookingStubs.Page(2, 45, AdminBookingStubs.Item(_confirmedId, "confirmed", new[] { "3A" }, 800)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);
        string tripAddress = $"/admin/bookings?tripId={BookingStubs.TripId}";

        // Act
        IDocument page = await browser.GetPageAsync($"{tripAddress}&page=2");

        // Assert
        Assert.Equal(new[] { "1", "2", "3" }, Texts(page, "[data-bookings-pages] a"));
        Assert.Equal(tripAddress, page.QuerySelector("[data-bookings-pages] a")!.GetAttribute("href"));
        Assert.Equal($"{tripAddress}&page=3", page.QuerySelectorAll("[data-bookings-pages] a").Last().GetAttribute("href"));
        Assert.Equal("2", page.QuerySelector("[data-bookings-pages] .active a")!.TextContent);
    }

    [Fact]
    public async Task Get_EmailAndTrip_SendsBothSoOnlyBookingsMatchingBothComeBack()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        await browser.GetPageAsync($"/admin/bookings?email=priya%40example.com&tripId={BookingStubs.TripId}");

        // Assert
        Assert.Equal($"{{\"bookingId\":null,\"contactEmail\":\"priya@example.com\",\"tripId\":\"{BookingStubs.TripId}\"}}", host.Booking.LastRequestBody);
    }

    [Fact]
    public async Task Get_NothingFound_SaysNoBookingsMatch()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings?email=nobody%40example.com");

        // Assert
        Assert.Equal(Messages.EmptySearch("bookings"), page.QuerySelector("[data-no-bookings]")!.TextContent);
    }

    [Fact]
    public async Task Get_NoCriteria_ShowsTheFormAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings");

        // Assert
        Assert.Equal("Bookings", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Find a booking by its ID, by the contact's email address, or by trip.", page.QuerySelector("[data-intro]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-bookings]"));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_EveryBoxEmpty_SaysToEnterACriterionAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings?bookingId=&email=&tripId=");

        // Assert
        Assert.Equal("Enter a booking ID, an email address or a trip ID.", page.QuerySelector("[data-criteria-error]")!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_FormWithEmptyBoxes_IsSentOnToTheSameSearchWithoutThem()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);

        // Act
        HttpResponseMessage response = await client.GetAsync("/admin/bookings?bookingId=&email=priya%40example.com&tripId=");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/bookings?email=priya%40example.com", response.Headers.Location!.OriginalString);
        Assert.Empty(host.Booking.Requests);
    }

    [Theory]
    [InlineData("email=not-an-email", "data-email-error", "email address")]
    [InlineData("bookingId=123", "data-booking-id-error", "booking ID")]
    [InlineData("tripId=abc", "data-trip-id-error", "trip ID")]
    public async Task Get_MalformedCriterion_ShowsTheFormatMessageOnThatBoxAndSendsNothing(string query, string marker, string field)
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync($"/admin/bookings?{query}");

        // Assert
        Assert.Equal(Messages.ValidationFormat(field), page.QuerySelector($"[{marker}]")!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_Result_LinksToTheBookingRememberingTheSearch()
    {
        // Arrange
        using FleetTestHost host = Host(2, () => AdminBookingStubs.Page(2, 45, AdminBookingStubs.Item(_confirmedId, "confirmed", new[] { "3A" }, 800)));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings?email=priya%40example.com&page=2");

        // Assert
        string href = page.QuerySelector("[data-open-booking]")!.GetAttribute("href")!;
        Assert.Equal($"/admin/bookings/{_confirmedId}?back=email%3Dpriya%2540example.com%26page%3D2", href);
    }

    [Fact]
    public async Task Get_BookingRefusesTheBox_ShowsTheFormatMessageOnIt()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => FakeBackend.ValidationError("tripId", "Not a trip."));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync($"/admin/bookings?tripId={BookingStubs.TripId}");

        // Assert
        Assert.Equal(Messages.ValidationFormat("trip ID"), page.QuerySelector("[data-trip-id-error]")!.TextContent);
        Assert.Equal(BookingStubs.TripId.ToString(), page.QuerySelector("#search-trip-id")!.GetAttribute("value"));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    public async Task Get_CustomerOrOperator_ShowsNoPermissionWithoutCallingBooking(string role)
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync("/admin/bookings");
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithTheSearchAsReturnAddress()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => AdminBookingStubs.Page(1, 0));
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync("/admin/bookings?email=priya%40example.com");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("returnUrl=/admin/bookings?email=priya%40example.com", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_BookingForbids_ShowsForbidden()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings?email=priya%40example.com");

        // Assert
        Assert.Equal(Messages.ProblemForbidden, BookingStubs.AlertText(page));
    }

    [Fact]
    public async Task Get_BookingDown_ShowsUpstreamAndKeepsWhatWasTyped()
    {
        // Arrange
        using FleetTestHost host = Host(1, () => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await browser.GetPageAsync("/admin/bookings?email=priya%40example.com");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Equal("priya@example.com", page.QuerySelector("#search-email")!.GetAttribute("value"));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    private static string[] Cells(IDocument page, Guid bookingId)
    {
        return page.QuerySelectorAll($"[data-booking='{bookingId}'] td").Select(cell => cell.TextContent.Trim()).ToArray();
    }

    private static FleetTestHost Host(int page, Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Booking.MapPathAndQuery(HttpMethod.Post, AdminBookingStubs.SearchQuery(page), answer);
        return host;
    }
}
