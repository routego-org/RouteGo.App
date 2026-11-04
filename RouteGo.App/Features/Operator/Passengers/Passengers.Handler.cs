namespace RouteGo.App.Features.Operator.Passengers;

/// <summary>One seat of a confirmed booking with the passenger who sits on it. Never the booking's contact.</summary>
public sealed record TripPassenger
{
    public required string SeatNo { get; init; }

    public required string SeatClass { get; init; }

    public required Guid BookingId { get; init; }

    public required string PassengerName { get; init; }

    public required int PassengerAge { get; init; }

    /// <summary>male, female or other.</summary>
    public required string PassengerGender { get; init; }
}

/// <summary>The passengers of one of the operator's trips, in the order Booking gives them (by seat).</summary>
public sealed record TripPassengerList
{
    public required BookingTrip Trip { get; init; }

    public required int SeatsBooked { get; init; }

    public required IReadOnlyList<TripPassenger> Passengers { get; init; }
}

public sealed class PassengersHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PassengersHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Every seat of every confirmed booking on the trip, for the trip's own operator.</summary>
    public async Task<ApiResult<TripPassengerList>> ListPassengersAsync(Guid tripId, CancellationToken cancellationToken)
    {
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Get, $"trips/{tripId}/passengers");
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<TripPassengerList>(cancellationToken);
    }

    /// <summary>
    /// "Passengers: Mumbai → Pune, 30 Oct 2026, 9:00 PM IST", or "Passengers: trip leaving …" when the
    /// address carries no route. The route is only a label the link passed.
    /// </summary>
    public static string Heading(string? route, DateTimeOffset departureAt)
    {
        string departure = IndiaTime.Format(departureAt);
        if (string.IsNullOrWhiteSpace(route))
        {
            return "Passengers: trip leaving " + departure;
        }

        return $"Passengers: {route.Trim()}, {departure}";
    }

    /// <summary>"3 seats booked." and "1 seat booked.".</summary>
    public static string SeatsBookedText(int seatsBooked)
    {
        if (seatsBooked == 1)
        {
            return "1 seat booked.";
        }

        return $"{seatsBooked} seats booked.";
    }
}
