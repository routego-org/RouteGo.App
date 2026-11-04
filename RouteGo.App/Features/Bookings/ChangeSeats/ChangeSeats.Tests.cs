using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.ChangeSeats;

public sealed partial class ChangeSeatsTests
{
    private static readonly string _pageAddress = $"{BookingStubs.PageAddress}/change-seats";
    private static readonly string _seatMapPath = $"/trips/{BookingStubs.TripId}/seats";

    [Fact]
    public async Task Get_Confirmed_OffersOnlyFreeSeatsOfTheSameClassAndSaysNoMoneyMoves()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C", "5D", "6A"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Change seats: Mumbai → Pune, 2 Oct 2026, 9:00 PM IST", page.QuerySelector("[data-heading]")!.TextContent);
        Assert.Equal("Passengers move with their seats. Fares stay the same. No money is charged or refunded.", page.QuerySelector("[data-no-money]")!.TextContent);
        Assert.Equal("Seat changes: 0 of 3 used", page.QuerySelector("[data-seat-changes]")!.TextContent);
        Assert.Equal(new[] { "3A", "3B" }, page.QuerySelectorAll("[data-seat-no]").Select(seat => seat.TextContent).ToArray());
        Assert.Equal("Priya Shah", page.QuerySelector("[data-seat-choice='3A'] [data-passenger]")!.TextContent);
        Assert.Equal(new[] { "Keep this seat", "5C", "5D", "6A" }, Options(page, "3A"));
        Assert.Equal(new[] { "Keep this seat", "5C", "5D", "6A" }, Options(page, "3B"));
        Assert.Equal("Bearer " + host.AccessToken, host.Booking.LastRequestHeader("Authorization"));
        Assert.Null(host.SeatInventory.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_AfterARefusal_KeepsStillValidPicksAndLeavesThemOutOfTheOtherSeatsChoices()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C", "5D", "6A"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => BookingStubs.Refusal(HttpStatusCode.UnprocessableEntity, "SEAT_CLASS_MISMATCH"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("5C", after.QuerySelector("[data-seat-choice='3A'] option[selected]")!.GetAttribute("value"));
        Assert.Equal(new[] { "Keep this seat", "5C", "5D", "6A" }, Options(after, "3A"));
        Assert.Equal(new[] { "Keep this seat", "5D", "6A" }, Options(after, "3B"));
    }

    [Fact]
    public async Task Change_OneSeat_SendsOneMoveAndShowsTheSuccessWithBothLinks()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C", "5D"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => Done(HttpStatusCode.Created, ("3A", "5C")));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.GetPageAsync(response.Headers.Location!.ToString());

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("{\"changes\":[{\"fromSeatNo\":\"3A\",\"toSeatNo\":\"5C\"}]}", host.Booking.LastRequestBody);
        Assert.Equal("Your seats were changed: 3A → 5C. Your ticket is being updated and will be sent to you again.", after.QuerySelector("[data-success]")!.TextContent);
        Assert.Equal(BookingStubs.PageAddress, after.QuerySelector("[data-view-booking]")!.GetAttribute("href"));
        Assert.Equal($"/tickets/{BookingStubs.BookingId}", after.QuerySelector("[data-view-ticket]")!.GetAttribute("href"));
        Assert.Null(after.QuerySelector("[data-change-form]"));
    }

    [Fact]
    public async Task Change_BothSeats_SendsBothMoves()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C", "5D"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => Done(HttpStatusCode.Created, ("3A", "5C"), ("3B", "5D")));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", "5D"));
        IDocument after = await browser.GetPageAsync(response.Headers.Location!.ToString());

        // Assert
        Assert.Contains("\"fromSeatNo\":\"3B\",\"toSeatNo\":\"5D\"", host.Booking.LastRequestBody);
        Assert.StartsWith("Your seats were changed: 3A → 5C, 3B → 5D.", after.QuerySelector("[data-success]")!.TextContent);
    }

    [Fact]
    public async Task Change_WhenEveryMoveWasAlreadyDone_ShowsTheChosenMovesAsSuccess()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => Done(HttpStatusCode.OK));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.GetPageAsync(response.Headers.Location!.ToString());

        // Assert
        Assert.StartsWith("Your seats were changed: 3A → 5C.", after.QuerySelector("[data-success]")!.TextContent);
    }

    [Fact]
    public async Task Change_NothingChosen_AsksForAChoiceAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("", ""));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Choose a new seat for at least one of your seats.", BookingStubs.AlertText(after));
        Assert.NotNull(after.QuerySelector("[data-change-form]"));
        Assert.DoesNotContain(host.Booking.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Change_SameNewSeatForTwoSeats_AsksForDifferentSeatsAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C", "5D"));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", "5C"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Choose a different new seat for each of your seats.", BookingStubs.AlertText(after));
        Assert.DoesNotContain(host.Booking.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Change_WhenANewSeatWasTakenMeanwhile_NamesItLoadsAgainAndKeepsTheOtherPick()
    {
        // Arrange
        bool isTaken = false;
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap(isTaken, "5C", "5D"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => BookingStubs.Refusal(HttpStatusCode.Conflict, "SEATS_UNAVAILABLE", "unavailableSeats", new[] { "5D" }));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);
        isTaken = true;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", "5D"));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("5D was just taken. Nothing was changed. We loaded the seats again.", BookingStubs.AlertText(after));
        Assert.Equal("5C", after.QuerySelector("[data-seat-choice='3A'] option[selected]")!.GetAttribute("value"));
        Assert.Equal(new[] { "Keep this seat" }, Options(after, "3B"));
    }

    [Theory]
    [InlineData("SEAT_CLASS_MISMATCH", "A seat can only be changed for one of the same class. Nothing was changed.")]
    [InlineData("SEATS_NOT_IN_BOOKING", "Some seats do not match this booking. We loaded it again. Nothing was changed.")]
    [InlineData("UNKNOWN_SEATS", "Some seats do not match this booking. We loaded it again. Nothing was changed.")]
    [InlineData("UPSTREAM_UNAVAILABLE", "We could not change your seats. Your booking still has its old seats. Please try again.")]
    [InlineData("UPSTREAM_TIMEOUT", "The change is taking longer than usual. Your booking still shows your old seats. Please check it in a few minutes before trying again.")]
    public async Task Change_Refused_ShowsTheWordsAndLoadsBookingAndSeatsAgain(string code, string expected)
    {
        // Arrange
        using FleetTestHost host = Host(new BookingStub().Answer, () => SeatMap("5C"));
        host.Booking.Map(HttpMethod.Post, BookingStubs.SeatChangesPath, () => BookingStubs.Refusal(Status(code), code));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(_pageAddress);
        int seatMapCalls = host.SeatInventory.Requests.Count;

        // Act
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-change-form]", Picks("5C", ""));
        IDocument after = await browser.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(expected, BookingStubs.AlertText(after));
        Assert.True(host.SeatInventory.Requests.Count > seatMapCalls);
        Assert.Equal(1, host.Booking.Requests.Count(request => request.Method == HttpMethod.Post));
    }

    private static FleetTestHost Host(Func<HttpResponseMessage> booking, Func<HttpResponseMessage> seatMap)
    {
        var host = new FleetTestHost();
        host.Booking.Map(HttpMethod.Get, BookingStubs.BookingPath, booking);
        host.SeatInventory.Map(HttpMethod.Get, _seatMapPath, seatMap);
        return host;
    }

    private static HttpResponseMessage Down()
    {
        throw new HttpRequestException("The service is down.");
    }

    private static string[] Options(IDocument page, string seatNo)
    {
        IElement select = page.QuerySelector($"[data-seat-choice='{seatNo}'] select")!;
        return select.QuerySelectorAll("option").Select(option => option.TextContent).ToArray();
    }

    private static Dictionary<string, string> Picks(string forFirst, string forSecond)
    {
        return new Dictionary<string, string> { ["Moves[3A]"] = forFirst, ["Moves[3B]"] = forSecond };
    }

    private static HttpStatusCode Status(string code)
    {
        if (code == "UPSTREAM_UNAVAILABLE")
        {
            return HttpStatusCode.BadGateway;
        }

        if (code == "UPSTREAM_TIMEOUT")
        {
            return HttpStatusCode.GatewayTimeout;
        }

        return HttpStatusCode.UnprocessableEntity;
    }

    private static HttpResponseMessage SeatMap(params string[] freeSeaters)
    {
        return SeatMap(false, freeSeaters);
    }

    // The trip's public seat map: the booking's own seats booked, the given Seater seats free, and a free Sleeper seat.
    private static HttpResponseMessage SeatMap(bool isLastSeaterTaken, params string[] freeSeaters)
    {
        var seats = new List<object>();
        seats.Add(new { seatNo = "3A", deck = "lower", row = 1, column = 1, seatClass = "Seater", fare = 800, status = "booked" });
        seats.Add(new { seatNo = "3B", deck = "lower", row = 1, column = 2, seatClass = "Seater", fare = 800, status = "booked" });
        for (int index = 0; index < freeSeaters.Length; index++)
        {
            bool isTaken = isLastSeaterTaken && index == freeSeaters.Length - 1;
            string status = "available";
            if (isTaken)
            {
                status = "booked";
            }

            seats.Add(new { seatNo = freeSeaters[index], deck = "lower", row = 2, column = index + 1, seatClass = "Seater", fare = 800, status });
        }

        seats.Add(new { seatNo = "L3", deck = "upper", row = 1, column = 1, seatClass = "Sleeper", fare = 1200, status = "available" });
        return FakeBackend.Json(new { tripId = BookingStubs.TripId, departureAt = "2026-10-02T15:30:00Z", isCancelled = false, isDeparted = false, seats });
    }

    private static HttpResponseMessage Done(HttpStatusCode status, params (string From, string To)[] moves)
    {
        var changes = new List<object>();
        foreach ((string from, string to) in moves)
        {
            changes.Add(new { fromSeatNo = from, toSeatNo = to });
        }

        return FakeBackend.Json(new { bookingId = BookingStubs.BookingId, changes, seatChangeCount = 1, seats = BookingStub.Seats() }, status);
    }
}
