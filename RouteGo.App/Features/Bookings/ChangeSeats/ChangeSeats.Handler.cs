namespace RouteGo.App.Features.Bookings.ChangeSeats;

/// <summary>One seat of the trip's public seat map; this page reads only what it needs to offer a free seat of the same class.</summary>
public sealed record FreeSeatCandidate
{
    public required string SeatNo { get; init; }

    public required string SeatClass { get; init; }

    /// <summary>available, held, booked or blocked.</summary>
    public required string Status { get; init; }
}

/// <summary>The trip's seat map as this page uses it.</summary>
public sealed record TripSeatMap
{
    public required bool IsCancelled { get; init; }

    public required bool IsDeparted { get; init; }

    public required IReadOnlyList<FreeSeatCandidate> Seats { get; init; }
}

/// <summary>One seat moved to another: the booking's seat and the free seat it moves to.</summary>
public sealed record SeatMove(string FromSeatNo, string ToSeatNo);

public sealed record SeatChangeRequest(IReadOnlyList<SeatMove> Changes);

/// <summary>Booking's answer to a seat change: the moves made by this request (none when every move was already done).</summary>
public sealed record SeatChangeAnswer
{
    public required IReadOnlyList<SeatMove> Changes { get; init; }
}

/// <summary>One of the booking's seats with the free seats it may move to and the one the person picked.</summary>
public sealed record SeatChoice
{
    public required BookingSeatDetail Seat { get; init; }

    public required IReadOnlyList<string> Candidates { get; init; }

    /// <summary>The new seat picked for this seat; empty when it stays where it is.</summary>
    public required string Chosen { get; init; }
}

/// <summary>The page as loaded: the booking with a choice per seat, or the message that says why there are no choices.</summary>
public sealed record ChangePageView
{
    public BookingDetail? Booking { get; init; }

    public IReadOnlyList<SeatChoice> Choices { get; init; } = Array.Empty<SeatChoice>();

    public PageAlert? Alert { get; init; }
}

/// <summary>What the page needs to load: which booking, when, and what the person has picked so far.</summary>
public sealed record ChangeSeatsQuery
{
    public required Guid BookingId { get; init; }

    public required DateTimeOffset Now { get; init; }

    public required string CorrelationId { get; init; }

    /// <summary>The picks to keep where they are still valid: booking seat → new seat.</summary>
    public required IReadOnlyDictionary<string, string> Picked { get; init; }
}

/// <summary>What a seat change came to: the moves made, or a message and whether to load the page again.</summary>
public sealed record ChangeOutcome
{
    /// <summary>"4C → 5C, 4D → 5D" when the change went through.</summary>
    public string? MovesText { get; init; }

    public PageAlert? Alert { get; init; }

    public bool ShouldReload { get; init; }
}

public sealed class ChangeSeatsHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ChangeSeatsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Loads the booking and, when it can be changed at all, the trip's seats. Only a confirmed
    /// booking whose trip is at least 30 minutes away, with seat changes left, is offered choices;
    /// anything else gets the message that says why and no seat map call.
    /// </summary>
    public async Task<ChangePageView> ViewAsync(ChangeSeatsQuery query, CancellationToken cancellationToken)
    {
        ApiResult<BookingDetail> api = await BookingApi.GetBookingAsync(_httpClientFactory, query.BookingId, cancellationToken);
        if (!api.IsSuccess)
        {
            return new ChangePageView { Alert = BookingProblems.ToLoadAlert(api, query.CorrelationId) };
        }

        BookingDetail booking = api.Value!;
        PageAlert? blocked = ChangeSeatsRefusals.NotOpenAlert(booking, query.Now);
        if (blocked != null)
        {
            return new ChangePageView { Booking = booking, Alert = blocked };
        }

        ApiResult<TripSeatMap> map = await GetSeatMapAsync(booking.Trip.TripId, cancellationToken);
        if (!map.IsSuccess)
        {
            return new ChangePageView { Booking = booking, Alert = ChangeSeatsRefusals.SeatMapAlert(map, query.BookingId, query.CorrelationId) };
        }

        PageAlert? tripAlert = ChangeSeatsRefusals.TripAlert(map.Value!, query.BookingId);
        if (tripAlert != null)
        {
            return new ChangePageView { Booking = booking, Alert = tripAlert };
        }

        return new ChangePageView { Booking = booking, Choices = ChangeSeatsChoices.Build(booking, map.Value!, query.Picked) };
    }

    /// <summary>
    /// Sends the moves, all or none. Never sent twice: a write is not retried, and a timeout is
    /// told as "may or may not have happened — check before trying again".
    /// </summary>
    public async Task<ChangeOutcome> ChangeAsync(Guid bookingId, IReadOnlyList<SeatMove> moves, string correlationId, CancellationToken cancellationToken)
    {
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Post, $"bookings/{bookingId}/seatChanges");
        request.Content = JsonContent.Create(new SeatChangeRequest(moves), options: WireJson.Options);
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        ApiResult<SeatChangeAnswer> api = await response.ReadApiResultAsync<SeatChangeAnswer>(cancellationToken);
        if (!api.IsSuccess)
        {
            return ChangeSeatsRefusals.ToOutcome(api, bookingId, correlationId);
        }

        // A 200 with no moves means every move was already done: the person's own moves are told.
        IReadOnlyList<SeatMove> made = api.Value!.Changes;
        if (made.Count == 0)
        {
            made = moves;
        }

        return new ChangeOutcome { MovesText = ChangeSeatsChoices.MovesText(made) };
    }

    // The seat map is public, so it is read without the person's token.
    private async Task<ApiResult<TripSeatMap>> GetSeatMapAsync(Guid tripId, CancellationToken cancellationToken)
    {
        HttpClient seatInventory = _httpClientFactory.CreateClient(HttpClients.SeatInventory);
        HttpResponseMessage response = await seatInventory.GetAsync($"trips/{tripId}/seats", cancellationToken);
        return await response.ReadApiResultAsync<TripSeatMap>(cancellationToken);
    }
}
