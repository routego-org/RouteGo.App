namespace RouteGo.App.Features.Bookings.Booking;

/// <summary>What the booking page offers for a booking as loaded: countdown, payment, giving up, and the confirmed booking's actions.</summary>
public sealed record BookingPageView
{
    public const string UsedAllSeatChanges = "You have used all 3 seat changes.";
    public const string SeatChangesClosed = "Seats can't be changed in the last 30 minutes before the trip leaves.";
    public const string TimeToPayEnded = "The time to pay has ended. If you already paid, we will confirm it or refund you.";

    public required BookingDetail Booking { get; init; }

    /// <summary>The seconds left on the hold while waiting; null when none is given or it has passed.</summary>
    public int? HoldSecondsLeft { get; init; }

    public bool IsWaiting { get; init; }

    /// <summary>Booking gave a payment page address and the hold is still running.</summary>
    public bool CanPayNow { get; init; }

    public bool IsConfirmed { get; init; }

    public bool HasTripLeft { get; init; }

    public bool CanChangeSeats { get; init; }

    /// <summary>Why seat changes are not offered on a confirmed booking whose trip has not left; null when they are, or the trip has left.</summary>
    public string? SeatChangesNote { get; init; }

    public bool CanCancel { get; init; }

    /// <summary>A booking that ended without a trip (failed, expired, cancelled) offers a way to search for buses again.</summary>
    public bool OffersSearch { get; init; }
}

public sealed class BookingPageHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BookingPageHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The booking as the signed-in person may see it.</summary>
    public async Task<ApiResult<BookingDetail>> GetBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        return await BookingApi.GetBookingAsync(_httpClientFactory, bookingId, cancellationToken);
    }

    /// <summary>
    /// Gives up an unpaid booking: Booking ends it and frees its seats at once. Giving up a booking
    /// already given up answers the same booking, so a second press is not an error.
    /// </summary>
    public async Task<ApiResult<BookingDetail>> ReleaseAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Post, $"bookings/{bookingId}/release");
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<BookingDetail>(cancellationToken);
    }

    /// <summary>
    /// What the page offers for the booking as it was loaded at <paramref name="now"/>. The 30-minute
    /// cutoff here only decides whether "Change seats" is offered; Booking still refuses a late one.
    /// </summary>
    public static BookingPageView Decide(BookingDetail booking, DateTimeOffset now)
    {
        if (BookingDisplay.IsWaiting(booking.Status))
        {
            return WaitingView(booking, now);
        }

        if (booking.Status == BookingDisplay.Confirmed)
        {
            return ConfirmedView(booking, now);
        }

        return new BookingPageView { Booking = booking, OffersSearch = true };
    }

    private static BookingPageView WaitingView(BookingDetail booking, DateTimeOffset now)
    {
        int? holdSecondsLeft = BookingDisplay.HoldSecondsLeft(booking.HoldExpiresAt, now);
        bool hasAddress = BookingDisplay.PaymentAddress(booking.CheckoutUrl) != null;
        return new BookingPageView
        {
            Booking = booking,
            IsWaiting = true,
            HoldSecondsLeft = holdSecondsLeft,
            CanPayNow = hasAddress && holdSecondsLeft != null,
        };
    }

    private static BookingPageView ConfirmedView(BookingDetail booking, DateTimeOffset now)
    {
        bool hasLeft = booking.Trip.DepartureAt <= now;
        if (hasLeft)
        {
            return new BookingPageView { Booking = booking, IsConfirmed = true, HasTripLeft = true };
        }

        string? note = SeatChangesNote(booking, now);
        return new BookingPageView
        {
            Booking = booking,
            IsConfirmed = true,
            CanCancel = true,
            CanChangeSeats = note is null,
            SeatChangesNote = note,
        };
    }

    // Null when seat changes are open: fewer than 3 used and the trip is 30 minutes or more away.
    private static string? SeatChangesNote(BookingDetail booking, DateTimeOffset now)
    {
        if (booking.SeatChangeCount >= BookingDisplay.MaxSeatChanges)
        {
            return BookingPageView.UsedAllSeatChanges;
        }

        TimeSpan untilDeparture = booking.Trip.DepartureAt - now;
        if (untilDeparture < TimeSpan.FromMinutes(BookingDisplay.CutoffMinutes))
        {
            return BookingPageView.SeatChangesClosed;
        }

        return null;
    }
}
