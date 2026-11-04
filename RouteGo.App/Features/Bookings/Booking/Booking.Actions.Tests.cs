using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.Booking;

public sealed class BookingActionsTests
{
    private const string CheckoutUrl = "https://pay.example/checkout/abc";

    [Fact]
    public async Task PayNow_WhileTheHoldRuns_SendsTheBrowserToTheAddressBookingGives()
    {
        // Arrange
        using FleetTestHost host = Host(() => Waiting().Answer());
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "form:has([data-pay-now])", new Dictionary<string, string>());

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(CheckoutUrl, response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task PayNow_WhenTheHoldEndedMeanwhile_ShowsTheBookingAgainWithoutRedirecting()
    {
        // Arrange
        bool holdEnded = false;
        using FleetTestHost host = Host(() => Waiting(holdEnded).Answer());
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);
        holdEnded = true;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "form:has([data-pay-now])", new Dictionary<string, string>());
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(after.QuerySelector("[data-time-to-pay-ended]"));
        Assert.Null(after.QuerySelector("[data-pay-now]"));
    }

    [Fact]
    public async Task Get_WaitingWithAPaymentAddressThatIsNotAWebAddress_OffersNoPayNow()
    {
        // Arrange
        var booking = new BookingStub { Status = "paymentPending", HoldExpiresAt = "2026-10-02T10:06:00Z", CheckoutUrl = "javascript:alert(1)", ConfirmedAt = null };
        using FleetTestHost host = Host(booking.Answer);
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(BookingStubs.PageAddress);

        // Assert
        Assert.Null(page.QuerySelector("[data-pay-now]"));
        Assert.NotNull(page.QuerySelector("[data-give-up]"));
    }

    [Fact]
    public async Task GiveUp_Link_AsksFirstWithBothChoicesAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(() => Waiting().Answer());
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync($"{BookingStubs.PageAddress}?giveUp=true");

        // Assert
        Assert.Equal("Give up this booking? Your seats will be released and you can book again straight away. This cannot be undone.", page.QuerySelector("[data-question]")!.TextContent);
        Assert.Equal("Give up booking", page.QuerySelector("[data-confirm-give-up]")!.TextContent);
        Assert.Equal("Keep booking", page.QuerySelector("[data-keep-booking]")!.TextContent);
        Assert.Equal(BookingStubs.PageAddress, page.QuerySelector("[data-keep-booking]")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-give-up]"));
        Assert.DoesNotContain(host.Booking.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task GiveUp_Confirmed_ReleasesTheBookingAndShowsItAbandoned()
    {
        // Arrange
        var released = new BookingStub { Status = "failed", FailureReason = "abandoned", ConfirmedAt = null };
        bool isReleased = false;
        using FleetTestHost host = Host(() => Answer(isReleased, released));
        host.Booking.Map(HttpMethod.Post, BookingStubs.ReleasePath, () =>
        {
            isReleased = true;
            return released.Answer();
        });
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument question = await browser.GetPageAsync($"{BookingStubs.PageAddress}?giveUp=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-give-up-question] form", new Dictionary<string, string>());
        IDocument after = await browser.GetPageAsync(response.Headers.Location!.ToString());

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(BookingStubs.PageAddress, response.Headers.Location!.ToString());
        Assert.Equal("You gave up this booking. Your seats were released.", after.QuerySelector("[data-flash]")!.TextContent.Trim());
        Assert.Equal("Failed", after.QuerySelector("[data-status]")!.TextContent);
        Assert.Equal("The booking was given up before payment.", after.QuerySelector("[data-outcome=failure]")!.TextContent);
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task GiveUp_WhenPaidMeanwhile_SaysSoAndLoadsTheBookingAgain()
    {
        // Arrange
        using FleetTestHost host = Host(() => Waiting().Answer());
        host.Booking.Map(HttpMethod.Post, BookingStubs.ReleasePath, () => BookingStubs.Refusal(HttpStatusCode.Conflict, "BOOKING_NOT_UNPAID", "bookingStatus", "confirmed"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument question = await browser.GetPageAsync($"{BookingStubs.PageAddress}?giveUp=true");
        int getsBefore = host.Booking.Requests.Count(request => request.Method == HttpMethod.Get);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-give-up-question] form", new Dictionary<string, string>());
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("This booking is already Confirmed. Nothing was changed.", BookingStubs.AlertText(after));
        Assert.True(host.Booking.Requests.Count(request => request.Method == HttpMethod.Get) > getsBefore);
        Assert.NotNull(after.QuerySelector("[data-status]"));
    }

    [Fact]
    public async Task GiveUp_WhenBookingIsDown_ShowsUpstreamMessageAndSendsTheReleaseOnce()
    {
        // Arrange
        using FleetTestHost host = Host(() => Waiting().Answer());
        host.Booking.Map(HttpMethod.Post, BookingStubs.ReleasePath, () => BookingStubs.Refusal(HttpStatusCode.BadGateway, "UPSTREAM_UNAVAILABLE"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument question = await browser.GetPageAsync($"{BookingStubs.PageAddress}?giveUp=true");

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(question, "[data-give-up-question] form", new Dictionary<string, string>());
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(after));
        Assert.Equal(1, host.Booking.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    private static BookingStub Waiting(bool holdEnded = false)
    {
        string holdExpiresAt = "2026-10-02T10:06:00Z";
        if (holdEnded)
        {
            holdExpiresAt = "2026-10-02T09:59:00Z";
        }

        return new BookingStub { Status = "paymentPending", HoldExpiresAt = holdExpiresAt, CheckoutUrl = CheckoutUrl, ConfirmedAt = null };
    }

    private static HttpResponseMessage Answer(bool isReleased, BookingStub released)
    {
        if (isReleased)
        {
            return released.Answer();
        }

        return Waiting().Answer();
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> answer)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingPath, answer);
        return host;
    }
}
