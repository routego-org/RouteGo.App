using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Tickets.Ticket;

public sealed class TicketTests
{
    private static readonly long _nowSeconds = FixedClock.Now.ToUnixTimeSeconds();
    private static readonly string _address = $"/tickets/{BookingStubs.BookingId}";

    [Fact]
    public async Task Get_ConfirmedWithStoredTicket_SendsTheBrowserStraightToTheLink()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: true);
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleCustomer);

        // Act
        HttpResponseMessage response = await client.GetAsync(_address);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith($"{FakeTicketStorage.LinkHost}/tickets/{BookingStubs.BookingId}.pdf", response.Headers.Location!.ToString());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
        Assert.Equal(BookingStubs.BookingPath, host.Booking.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Get_EveryVisit_MakesANewLinkThatWorksForFiveMinutes()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: true);
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleCustomer);

        // Act
        HttpResponseMessage first = await client.GetAsync(_address);
        HttpResponseMessage second = await client.GetAsync(_address);

        // Assert
        long expires = _nowSeconds + 300;
        Assert.NotEqual(first.Headers.Location, second.Headers.Location);
        Assert.Contains($"expires={expires}", first.Headers.Location!.ToString());
        Assert.Equal(2, host.TicketStorage.LinkTimes.Count);
    }

    [Fact]
    public async Task Get_AdminOpeningACustomersTicket_OpensIt()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: true);
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleAdmin);

        // Act
        HttpResponseMessage response = await client.GetAsync(_address);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(host.TicketStorage.LinkTimes);
    }

    [Fact]
    public async Task Get_ConfirmedButNotStoredYet_ShowsPreparingAndChecksAgainEveryFiveSeconds()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: false);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.Equal("Your ticket is being prepared. This usually takes a few seconds.", page.QuerySelector("[data-wait-text]")!.TextContent);
        Assert.Equal($"5;url={_address}?since={_nowSeconds}", page.QuerySelector("[data-refresh]")!.GetAttribute("content"));
        Assert.Equal("Your ticket · RouteGo", page.Title);
        Assert.Empty(host.TicketStorage.LinkTimes);
    }

    [Fact]
    public async Task Get_PreparingThenStored_NextCheckSendsTheBrowserToTheLink()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: false);
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleCustomer);
        long started = _nowSeconds - 10;

        // Act
        HttpResponseMessage waiting = await client.GetAsync($"{_address}?since={started}");
        host.TicketStorage.Store(BookingStubs.BookingId);
        HttpResponseMessage ready = await client.GetAsync($"{_address}?since={started}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, waiting.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, ready.StatusCode);
    }

    [Fact]
    public async Task Get_StillNotStoredAfterTwoMinutes_StopsCheckingAndOffersTryAgain()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: false);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{_address}?since={_nowSeconds - 120}");

        // Assert
        Assert.StartsWith(TicketPageHandler.TakingLonger, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-refresh]"));
        Assert.Equal(_address, page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Contains(Messages.LinkTryAgain, BookingStubs.AlertText(page));
        Assert.Contains(Messages.AccessGoHome, BookingStubs.AlertText(page));
    }

    [Fact]
    public async Task Get_StartTimeInTheFuture_StartsANewTwoMinutes()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: false);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{_address}?since={_nowSeconds + 3600}");

        // Assert
        Assert.Equal($"5;url={_address}?since={_nowSeconds}", page.QuerySelector("[data-refresh]")!.GetAttribute("content"));
    }

    [Fact]
    public async Task Get_CancelledBookingWithStoredTicket_ShowsCancelledAndMakesNoLink()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { Status = "cancelled" }.Answer, stored: true);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.StartsWith(TicketPageHandler.Cancelled, BookingStubs.AlertText(page));
        Assert.Equal("/", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal(0, host.TicketStorage.Checks);
        Assert.Empty(host.TicketStorage.LinkTimes);
    }

    [Theory]
    [InlineData("initiated")]
    [InlineData("seatsHeld")]
    [InlineData("paymentPending")]
    public async Task Get_NotConfirmedYet_SaysSoAndDoesNotCheckAgain(string status)
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { Status = status }.Answer, stored: false);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.StartsWith(TicketPageHandler.NotConfirmedYet, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-refresh]"));
        Assert.Equal(0, host.TicketStorage.Checks);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("expired")]
    public async Task Get_FailedOrExpired_SaysItHasNoTicket(string status)
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub { Status = status }.Answer, stored: false);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.StartsWith(TicketPageHandler.NoTicket, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-refresh]"));
    }

    [Fact]
    public async Task Get_AnotherCustomersBooking_ShowsForbiddenAndMakesNoLink()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Forbidden, "FORBIDDEN"), stored: true);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.StartsWith(Messages.ProblemForbidden, BookingStubs.AlertText(page));
        Assert.Equal("/", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Empty(host.TicketStorage.LinkTimes);
    }

    [Fact]
    public async Task Get_UnknownBookingOrNotAnIdentifier_ShowsNotFoundAndOnlyCallsBookingForTheFirst()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"), stored: true);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument unknown = await browser.GetPageAsync(_address);
        int callsAfterUnknown = host.Booking.Requests.Count;
        IDocument notAnId = await browser.GetPageAsync("/tickets/B-404");

        // Assert
        Assert.StartsWith(Messages.ProblemNotFound("booking"), BookingStubs.AlertText(unknown));
        Assert.Equal("/", unknown.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.StartsWith(Messages.ProblemNotFound("booking"), BookingStubs.AlertText(notAnId));
        Assert.Equal(callsAfterUnknown, host.Booking.Requests.Count);
    }

    [Fact]
    public async Task Get_BookingDown_ShowsUpstreamMessage()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"), stored: true);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.StartsWith(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
    }

    [Fact]
    public async Task Get_TicketStorageDown_ShowsUpstreamMessageAndMakesNoLink()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, stored: true);
        host.TicketStorage.IsUnavailable = true;
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_address);

        // Assert
        Assert.StartsWith(Messages.ProblemUpstream, BookingStubs.AlertText(page));
        Assert.Null(page.QuerySelector("[data-refresh]"));
        Assert.Empty(host.TicketStorage.LinkTimes);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithThisAddressAsReturnAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(_address);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"returnUrl={_address}", Uri.UnescapeDataString(response.Headers.Location!.ToString()));
        Assert.Empty(host.Booking.Requests);
    }

    [Fact]
    public async Task Get_Operator_ShowsNoPermissionWithoutCallingBookingOrStorage()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage response = await client.GetAsync(_address);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(Messages.AccessNoPermission, page.Body!.TextContent);
        Assert.Empty(host.Booking.Requests);
        Assert.Equal(0, host.TicketStorage.Checks);
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> booking, bool stored)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingPath, booking);
        if (stored)
        {
            host.TicketStorage.Store(BookingStubs.BookingId);
        }

        return host;
    }
}
