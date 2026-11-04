using System.Text.Json;
using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.BookSeats;

public sealed class BookSeatsPayTests
{
    [Fact]
    public async Task Pay_FilledForm_SendsTheBookingWithTheExpectedTotalAndGoesStraightToThePaymentPage()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookSeatsStubs.Created(BookSeatsStubs.CheckoutUrl));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(BookSeatsStubs.CheckoutUrl, response.Headers.Location!.ToString());
        using var body = JsonDocument.Parse(host.Booking.LastRequestBody!);
        JsonElement root = body.RootElement;
        Assert.Equal(BookingStubs.TripId.ToString(), root.GetProperty("tripId").GetString());
        Assert.Equal("Priya Shah", root.GetProperty("contactName").GetString());
        Assert.Equal(1600, root.GetProperty("expectedTotal").GetInt32());
        JsonElement second = root.GetProperty("seats")[1];
        Assert.Equal("3B", second.GetProperty("seatNo").GetString());
        Assert.Equal("Ravi Shah", second.GetProperty("passengerName").GetString());
        Assert.Equal(36, second.GetProperty("passengerAge").GetInt32());
        Assert.Equal("male", second.GetProperty("passengerGender").GetString());
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
        Assert.DoesNotContain(host.Booking.Requests, request => request.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Pay_AnswerWithoutAPaymentAddress_GoesToTheBookingPage()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookSeatsStubs.Created(null));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));

        // Assert
        Assert.Equal($"/my-bookings/{BookingStubs.BookingId}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Pay_AnswerWithAPaymentAddressThatIsNotAWebAddress_GoesToTheBookingPageInstead()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookSeatsStubs.Created("javascript:alert(1)"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));

        // Assert
        Assert.Equal($"/my-bookings/{BookingStubs.BookingId}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Pay_PriceChanged_ShowsTheNewFaresAndTotalKeepsTheFormAndSendsTheNewTotalNextTime()
    {
        // Arrange
        bool hasChanged = false;
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap(false, false, 800));
        host.Booking.Map(HttpMethod.Post, BookingStubs.BookingsPath, () => PriceChangedOrCreated(ref hasChanged));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage first = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(first);
        List<KeyValuePair<string, string>> again = BookSeatsStubs.With(BookSeatsStubs.Form("3A", "3B"), "Passengers[3A].Fare", after.QuerySelector("[name='Passengers[3A].Fare']")!.GetAttribute("value")!);
        HttpResponseMessage second = await browser.SendFormAsync(after, "[data-book-form]", again);

        // Assert
        Assert.Equal("The price changed. The new total is ₹1,700. Please check it and choose Pay again.", BookingStubs.AlertText(after));
        Assert.Equal("₹900", after.QuerySelector("[data-seat='3A'] [data-fare]")!.TextContent);
        Assert.Equal("Total ₹1,700", after.QuerySelector("[data-total]")!.TextContent);
        Assert.Equal("Pay ₹1,700", after.QuerySelector("[data-pay]")!.TextContent);
        Assert.Equal(new[] { "3A", "3B" }, BookSeatsStubs.Chosen(after));
        Assert.Equal("Ravi Shah", after.QuerySelector("[name='Passengers[3B].Name']")!.GetAttribute("value"));
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Contains("\"expectedTotal\":1700", host.Booking.LastRequestBody);
    }

    [Fact]
    public async Task Pay_ASeatWasTaken_NamesItLoadsSeatsAgainAndKeepsTheOtherSeat()
    {
        // Arrange
        bool isTaken = false;
        using FleetTestHost host = BookSeatsStubs.Host(() => SeatsAfter(isTaken));
        host.Booking.Map(HttpMethod.Post, BookingStubs.BookingsPath, () => BookingStubs.Refusal(HttpStatusCode.Conflict, "SEATS_UNAVAILABLE", "unavailableSeats", new[] { "3B" }));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);
        isTaken = true;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("3B was just taken by someone else. Please choose other seats.", BookingStubs.AlertText(after));
        Assert.Equal("Booked", after.QuerySelector("[data-seat='3B'] [data-status-label]")!.TextContent);
        Assert.Equal(new[] { "3A" }, BookSeatsStubs.Chosen(after));
        Assert.Equal("Priya Shah", after.QuerySelector("[name='Passengers[3A].Name']")!.GetAttribute("value"));
        Assert.Equal("Total ₹800", after.QuerySelector("[data-total]")!.TextContent);
    }

    [Fact]
    public async Task Pay_SeveralSeatsTaken_UsesWere()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Conflict, "SEATS_UNAVAILABLE", "unavailableSeats", new[] { "3A", "3B" }));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("3A, 3B were just taken by someone else. Please choose other seats.", BookingStubs.AlertText(after));
        Assert.Empty(BookSeatsStubs.Chosen(after));
    }

    [Fact]
    public async Task Pay_AnUnpaidBookingExists_OffersItAndKeepsTypedValues()
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["bookingId"] = "77777777-0000-4000-8000-000000000077", ["holdExpiresAt"] = "2026-10-02T10:08:00Z" };
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Conflict, "UNPAID_BOOKING_EXISTS", extensions));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("You already have a booking waiting for payment. Its seats are held until 2 Oct 2026, 3:38 PM IST.", BookingStubs.AlertText(after));
        Assert.Equal("Continue with that booking", after.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal(BookingStubs.PageAddress, after.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal("Ravi Shah", after.QuerySelector("[name='Passengers[3B].Name']")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Pay_BookingClosed_ShowsTheDepartureAndCutoffBookingGives()
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["departureAt"] = "2026-10-02T15:30:00Z", ["cutoffMinutes"] = 30 };
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.Conflict, "BOOKING_CLOSED", extensions));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("This can't be done any more. The trip leaves at 2 Oct 2026, 9:00 PM IST, and booking and seat changes close 30 minutes before. Nothing was changed.", BookingStubs.AlertText(after));
    }

    [Fact]
    public async Task Pay_TripCancelledMeanwhile_ShowsTheRefusalAndTheNoticeAndNoForm()
    {
        // Arrange
        bool isCancelled = false;
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap(isCancelled, false, 800));
        host.Booking.Map(HttpMethod.Post, BookingStubs.BookingsPath, () => BookingStubs.Refusal(HttpStatusCode.Conflict, "TRIP_CANCELLED"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);
        isCancelled = true;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemTripCancelled, BookingStubs.AlertText(after));
        Assert.Equal(Messages.NoticeTripCancelled, after.QuerySelector("[data-notice]")!.TextContent);
        Assert.Null(after.QuerySelector("[data-book-form]"));
    }

    [Fact]
    public async Task Pay_BookingDoesNotKnowTheTripYet_SaysSoWithTryAgainAndKeepsTypedValues()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("This trip can't be booked yet. If it was put on sale just now, try again in a minute.", BookingStubs.AlertText(after));
        Assert.Equal("Try again", after.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal(new[] { "3A" }, BookSeatsStubs.Chosen(after));
        Assert.Equal("Priya Shah", after.QuerySelector("[name='Passengers[3A].Name']")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Pay_SeatsTheTripDoesNotHave_SaysSoAndLoadsTheSeatsAgain()
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(HttpStatusCode.UnprocessableEntity, "UNKNOWN_SEATS", "unknownSeats", new[] { "3A" }));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);
        int seatMapCalls = host.SeatInventory.Requests.Count;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Some seats are no longer on this trip. We loaded the seats again. Please choose again.", BookingStubs.AlertText(after));
        Assert.True(host.SeatInventory.Requests.Count > seatMapCalls);
        Assert.Equal(new[] { "3B" }, BookSeatsStubs.Chosen(after));
    }

    [Fact]
    public async Task Pay_BookingRefusesAField_ShowsItsMessageUnderThatPassengerAndKeepsEverything()
    {
        // Arrange
        using FleetTestHost host = Host(() => FakeBackend.ValidationError("seats[0].passengerAge", "Passenger age must be between 0 and 120."));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Passenger age must be between 0 and 120.", BookSeatsStubs.ErrorOf(after, "3A|age"));
        Assert.Equal("34", after.QuerySelector("[name='Passengers[3A].Age']")!.GetAttribute("value"));
        Assert.Equal(new[] { "3A", "3B" }, BookSeatsStubs.Chosen(after));
    }

    [Theory]
    [InlineData("UPSTREAM_UNAVAILABLE", HttpStatusCode.BadGateway)]
    [InlineData("UPSTREAM_TIMEOUT", HttpStatusCode.GatewayTimeout)]
    public async Task Pay_BookingCouldNotFinish_SaysNoSeatsWereKeptAndKeepsTheForm(string code, HttpStatusCode status)
    {
        // Arrange
        using FleetTestHost host = Host(() => BookingStubs.Refusal(status, code));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("We could not complete your booking. No seats were kept for you. Please try again.", BookingStubs.AlertText(after));
        Assert.Equal(new[] { "3A", "3B" }, BookSeatsStubs.Chosen(after));
        Assert.Equal("Ravi Shah", after.QuerySelector("[name='Passengers[3B].Name']")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Pay_BookingCannotBeReached_ShowsUpstreamMessageKeepsTheFormAndSendsOnce()
    {
        // Arrange
        using FleetTestHost host = Host(() => throw new HttpRequestException("The service is down."));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", BookSeatsStubs.Form("3A", "3B"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, BookingStubs.AlertText(after));
        Assert.Equal(new[] { "3A", "3B" }, BookSeatsStubs.Chosen(after));
        Assert.Equal(1, BookSeatsStubs.BookingPosts(host));
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> bookingAnswer)
    {
        FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap());
        host.Booking.Map(HttpMethod.Post, BookingStubs.BookingsPath, bookingAnswer);
        return host;
    }

    private static HttpResponseMessage SeatsAfter(bool isTaken)
    {
        if (isTaken)
        {
            return BookSeatsStubs.SeatMapWithBooked("3B", "3A", "3C");
        }

        return BookSeatsStubs.SeatMap();
    }

    // The first booking request meets a price change (3A now ₹900); the next one is accepted.
    private static HttpResponseMessage PriceChangedOrCreated(ref bool hasChanged)
    {
        if (hasChanged)
        {
            return BookSeatsStubs.Created(BookSeatsStubs.CheckoutUrl);
        }

        hasChanged = true;
        var extensions = new Dictionary<string, object>
        {
            ["currentTotal"] = 1700,
            ["seatFares"] = new object[] { new { seatNo = "3A", fare = 900 }, new { seatNo = "3B", fare = 800 } },
        };
        return BookingStubs.Refusal(HttpStatusCode.Conflict, "PRICE_CHANGED", extensions);
    }
}
