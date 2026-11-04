namespace RouteGo.App.Features.Seats.PublicSeatMap;

/// <summary>One seat of the public seat map, as Seat Inventory gives it.</summary>
public sealed record MapSeat
{
    public required string SeatNo { get; init; }

    /// <summary>lower or upper.</summary>
    public required string Deck { get; init; }

    public required int Row { get; init; }

    public required int Column { get; init; }

    public required string SeatClass { get; init; }

    /// <summary>Today's fare in whole rupees.</summary>
    public required int Fare { get; init; }

    /// <summary>available, held, booked or blocked.</summary>
    public required string Status { get; init; }
}

/// <summary>A trip's public seat map: every seat, deck by deck, and whether the trip was cancelled or has left.</summary>
public sealed record SeatMap
{
    public required Guid TripId { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    public required bool IsCancelled { get; init; }

    /// <summary>Whether the trip had left when Seat Inventory answered.</summary>
    public required bool IsDeparted { get; init; }

    public required IReadOnlyList<MapSeat> Seats { get; init; }
}

public sealed class PublicSeatMapHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PublicSeatMapHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The trip's seat map. Sent without the person's access token: the page is open to everyone
    /// and Seat Inventory's seat map is public.
    /// </summary>
    public async Task<ApiResult<SeatMap>> GetSeatMapAsync(Guid tripId, CancellationToken cancellationToken)
    {
        HttpClient seatInventory = _httpClientFactory.CreateClient(HttpClients.SeatInventory);
        HttpResponseMessage response = await seatInventory.GetAsync($"trips/{tripId}/seats", cancellationToken);
        return await response.ReadApiResultAsync<SeatMap>(cancellationToken);
    }
}

public sealed class PublicSeatMapModel : PageModel
{
    private const string HomeAddress = "/";

    private readonly PublicSeatMapHandler _handler;

    public PublicSeatMapModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new PublicSeatMapHandler(httpClientFactory);
    }

    /// <summary>The trip named in the address, as typed: an address that names no trip is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? TripId { get; set; }

    /// <summary>The route label a "See seats" link passed ("Mumbai → Pune"); only shown in the heading.</summary>
    [BindProperty(SupportsGet = true, Name = "route")]
    public string? RouteName { get; set; }

    /// <summary>The loaded seat map; null when it could not be loaded.</summary>
    public SeatMap? Map { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(TripId, out Guid tripId))
        {
            Alert = SeatDisplay.UnavailableAlert(SameAddress(), Messages.AccessGoHome, HomeAddress);
            return;
        }

        ApiResult<SeatMap> api = await _handler.GetSeatMapAsync(tripId, cancellationToken);
        if (api.IsSuccess)
        {
            Map = api.Value;
            return;
        }

        if (SeatDisplay.IsUnavailable(api))
        {
            Alert = SeatDisplay.UnavailableAlert(SameAddress(), Messages.AccessGoHome, HomeAddress);
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }

    /// <summary>"Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST".</summary>
    public string Heading()
    {
        return SeatDisplay.Heading(RouteName, Map!.DepartureAt);
    }

    /// <summary>"1 of 3 seats available."; null for a cancelled or departed trip, which shows its notice instead.</summary>
    public string? AvailableText()
    {
        if (Map!.IsCancelled || Map.IsDeparted)
        {
            return null;
        }

        int available = Map.Seats.Count(seat => seat.Status == SeatDisplay.Available);
        return $"{available} of {Map.Seats.Count} seats available.";
    }

    /// <summary>
    /// Whether "Book these seats" is offered: to a visitor or a signed-in customer, for a trip that is
    /// neither cancelled nor departed and has at least one Available seat. Operators and admins never book.
    /// </summary>
    public bool OffersBooking()
    {
        bool isSignedIn = User.Identity != null && User.Identity.IsAuthenticated;
        bool mayBook = !isSignedIn || User.IsInRole(PortalSession.RoleCustomer);
        bool canBeBooked = !Map!.IsCancelled && !Map.IsDeparted;
        bool hasFreeSeat = Map.Seats.Any(seat => seat.Status == SeatDisplay.Available);
        return mayBook && canBeBooked && hasFreeSeat;
    }

    /// <summary>The booking page of this trip, passing the route label on.</summary>
    public string BookAddress()
    {
        return BookingDisplay.BookSeatsAddress(Map!.TripId, RouteName);
    }

    /// <summary>The seats to draw; none can be picked on this page.</summary>
    public IReadOnlyList<SeatDisplay.DeckLayout> Grid()
    {
        var seats = new List<SeatDisplay.Seat>();
        foreach (MapSeat seat in Map!.Seats)
        {
            seats.Add(ToSeat(seat));
        }

        return SeatDisplay.ToGrid(seats);
    }

    private string SameAddress()
    {
        return $"{Request.PathBase}{Request.Path}{Request.QueryString}";
    }

    private static SeatDisplay.Seat ToSeat(MapSeat seat)
    {
        return new SeatDisplay.Seat
        {
            SeatNo = seat.SeatNo,
            Deck = seat.Deck,
            Row = seat.Row,
            Column = seat.Column,
            SeatClass = seat.SeatClass,
            Fare = seat.Fare,
            Status = seat.Status,
        };
    }
}
