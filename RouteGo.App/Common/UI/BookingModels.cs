namespace RouteGo.App.Common.UI;

/// <summary>The trip of a booking, as Booking gives it.</summary>
public sealed record BookingTrip
{
    public required Guid TripId { get; init; }

    public required string SourceCityName { get; init; }

    public required string DestinationCityName { get; init; }

    /// <summary>The travel date in IST.</summary>
    public required DateOnly ServiceDate { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    public required DateTimeOffset ArrivalAt { get; init; }

    public required string BusName { get; init; }

    /// <summary>seater, sleeper or semiSleeper.</summary>
    public required string BusType { get; init; }

    public required string OperatorName { get; init; }
}

/// <summary>One seat of a booking with the passenger who sits on it.</summary>
public sealed record BookingSeatDetail
{
    public required string SeatNo { get; init; }

    public required string SeatClass { get; init; }

    /// <summary>The fare this seat was sold at, in whole rupees.</summary>
    public required int Fare { get; init; }

    public required string PassengerName { get; init; }

    public required int PassengerAge { get; init; }

    /// <summary>male, female or other.</summary>
    public required string PassengerGender { get; init; }
}

/// <summary>The money Booking gave back for a booking, and how far that refund has got.</summary>
public sealed record BookingRefundInfo
{
    /// <summary>cancellation, latePayment, seatsLost or tripCancelled.</summary>
    public required string Reason { get; init; }

    public required int Amount { get; init; }

    /// <summary>requested or completed.</summary>
    public required string Status { get; init; }
}

/// <summary>Who cancelled a booking, why, and the refund share it got.</summary>
public sealed record BookingCancellationInfo
{
    /// <summary>customerCancelled, adminCancelled or operatorCancelled.</summary>
    public required string Kind { get; init; }

    /// <summary>The admin's or the bus company's reason; null for a customer's own cancellation.</summary>
    public string? Reason { get; init; }

    public required int RefundPercent { get; init; }

    public required int RefundAmount { get; init; }

    public required DateTimeOffset CancelledAt { get; init; }
}

/// <summary>
/// One booking as Booking's get-one-booking answer gives it. The payment page address (CheckoutUrl)
/// is only ever followed by a redirect: it is never written to a page, a log line or the session.
/// </summary>
public sealed record BookingDetail
{
    public required Guid BookingId { get; init; }

    /// <summary>initiated, seatsHeld, paymentPending, confirmed, failed, expired or cancelled.</summary>
    public required string Status { get; init; }

    public required BookingTrip Trip { get; init; }

    public required string ContactName { get; init; }

    public required string ContactEmail { get; init; }

    public required IReadOnlyList<BookingSeatDetail> Seats { get; init; }

    public required int TotalAmount { get; init; }

    /// <summary>When the hold ends unless paid; given only while the booking is unpaid.</summary>
    public DateTimeOffset? HoldExpiresAt { get; init; }

    public string? CheckoutUrl { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>Payment's own words for a declined payment.</summary>
    public string? PaymentFailureDetail { get; init; }

    public required int SeatChangeCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ConfirmedAt { get; init; }

    public BookingCancellationInfo? Cancellation { get; init; }

    public BookingRefundInfo? Refund { get; init; }
}

/// <summary>The calls every booking page makes the same way.</summary>
public static class BookingApi
{
    /// <summary>One booking, as the signed-in person may see it.</summary>
    public static async Task<ApiResult<BookingDetail>> GetBookingAsync(
        IHttpClientFactory httpClientFactory, Guid bookingId, CancellationToken cancellationToken)
    {
        HttpClient booking = httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Get, $"bookings/{bookingId}");
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<BookingDetail>(cancellationToken);
    }
}
