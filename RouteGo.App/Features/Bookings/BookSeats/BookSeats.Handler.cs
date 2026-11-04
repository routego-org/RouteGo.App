using System.Globalization;

namespace RouteGo.App.Features.Bookings.BookSeats;

/// <summary>One seat of the trip's public seat map, as Seat Inventory gives it.</summary>
public sealed record BookableSeat
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

/// <summary>The trip's public seat map: every seat, and whether the trip was cancelled or has left.</summary>
public sealed record BookableMap
{
    public required DateTimeOffset DepartureAt { get; init; }

    public required bool IsCancelled { get; init; }

    public required bool IsDeparted { get; init; }

    public required IReadOnlyList<BookableSeat> Seats { get; init; }
}

/// <summary>The part of Identity's profile that fills the contact name.</summary>
public sealed record ProfileName
{
    public required string FullName { get; init; }
}

/// <summary>The seat map with the name to put in the contact box; the name is empty when Identity gave none.</summary>
public sealed record BookSeatsLoad(ApiResult<BookableMap> Map, string ContactName);

/// <summary>One seat of the booking request: the seat and the passenger who sits on it.</summary>
public sealed record BookingSeatRequest
{
    public required string SeatNo { get; init; }

    public required string PassengerName { get; init; }

    public required int PassengerAge { get; init; }

    public required string PassengerGender { get; init; }
}

public sealed record CreateBookingRequest
{
    public required Guid TripId { get; init; }

    public required string ContactName { get; init; }

    public required IReadOnlyList<BookingSeatRequest> Seats { get; init; }

    /// <summary>The total the customer saw, compared with the current fares and never charged.</summary>
    public required int ExpectedTotal { get; init; }
}

/// <summary>A booking Booking created: its identity and where to pay. The address is only ever followed, never shown or kept.</summary>
public sealed record CreatedBooking
{
    public required Guid BookingId { get; init; }

    public string? CheckoutUrl { get; init; }
}

/// <summary>What "Pay" sends: the trip, the chosen seats with what was typed for them, and where the page is.</summary>
public sealed record PayCommand
{
    public required Guid TripId { get; init; }

    public required IReadOnlyList<string> Seats { get; init; }

    public required IReadOnlyDictionary<string, PassengerInput> Passengers { get; init; }

    public required string ContactName { get; init; }

    /// <summary>The booking page's own address, offered as "Try again" when the trip cannot be booked yet.</summary>
    public required string SameAddress { get; init; }

    public required string CorrelationId { get; init; }
}

public sealed class BookSeatsHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BookSeatsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The seat map and the profile's full name, asked together. A profile that cannot be read
    /// leaves the contact name empty and says nothing: the person can type it.
    /// </summary>
    public async Task<BookSeatsLoad> LoadAsync(Guid tripId, CancellationToken cancellationToken)
    {
        Task<ApiResult<BookableMap>> mapTask = GetSeatMapAsync(tripId, cancellationToken);
        Task<string> nameTask = GetContactNameAsync(cancellationToken);
        await Task.WhenAll(mapTask, nameTask);
        return new BookSeatsLoad(await mapTask, await nameTask);
    }

    /// <summary>
    /// The trip's seat map, sent without the person's access token: the seat map is public.
    /// </summary>
    public async Task<ApiResult<BookableMap>> GetSeatMapAsync(Guid tripId, CancellationToken cancellationToken)
    {
        HttpClient seatInventory = _httpClientFactory.CreateClient(HttpClients.SeatInventory);
        HttpResponseMessage response = await seatInventory.GetAsync($"trips/{tripId}/seats", cancellationToken);
        return await response.ReadApiResultAsync<BookableMap>(cancellationToken);
    }

    /// <summary>
    /// Asks Booking to create the booking: check the price, hold the seats, start the payment. Sent
    /// once and never repeated by the portal; a lost answer is recovered through "you already have a
    /// booking waiting for payment".
    /// </summary>
    public async Task<ApiResult<CreatedBooking>> CreateBookingAsync(CreateBookingRequest body, CancellationToken cancellationToken)
    {
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Post, "bookings");
        request.Content = JsonContent.Create(body, options: WireJson.Options);
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<CreatedBooking>(cancellationToken);
    }

    /// <summary>
    /// Checks the typed form, then asks Booking to book. Nothing is sent when a field fails its check
    /// or the page does not know the fares the customer saw; otherwise the outcome is the redirect
    /// (to the payment page, or to the booking when Booking gave no address) or what the refusal means.
    /// </summary>
    public async Task<PayOutcome> PayAsync(PayCommand command, CancellationToken cancellationToken)
    {
        Dictionary<string, string> errors = BookSeatsChecks.Check(command.Seats, command.Passengers, command.ContactName);
        if (errors.Count > 0)
        {
            return new PayOutcome { FieldErrors = errors };
        }

        int? total = ExpectedTotal(command);
        if (total is null)
        {
            return new PayOutcome { Alert = PageAlert.Danger(Messages.ProblemFormExpired) };
        }

        ApiResult<CreatedBooking> api = await CreateBookingAsync(ToRequest(command, total.Value), cancellationToken);
        if (api.IsSuccess)
        {
            return new PayOutcome { RedirectTo = RedirectAddress(api.Value!) };
        }

        return BookSeatsRefusals.ToOutcome(api, command);
    }

    // The payment page address Booking gave, or the booking's own page when it gave none.
    private static string RedirectAddress(CreatedBooking created)
    {
        string? payment = BookingDisplay.PaymentAddress(created.CheckoutUrl);
        if (payment is null)
        {
            return BookingDisplay.BookingAddress(created.BookingId);
        }

        return payment;
    }

    // The sum of the fares the customer saw: the ones the page posted with each chosen seat.
    private static int? ExpectedTotal(PayCommand command)
    {
        int total = 0;
        foreach (string seatNo in command.Seats)
        {
            int? fare = BookSeatsChecks.FareOf(BookSeatsChecks.PassengerOf(command.Passengers, seatNo));
            if (fare is null)
            {
                return null;
            }

            total += fare.Value;
        }

        return total;
    }

    private static CreateBookingRequest ToRequest(PayCommand command, int expectedTotal)
    {
        var seats = new List<BookingSeatRequest>();
        foreach (string seatNo in command.Seats)
        {
            PassengerInput passenger = BookSeatsChecks.PassengerOf(command.Passengers, seatNo);
            seats.Add(new BookingSeatRequest
            {
                SeatNo = seatNo,
                PassengerName = passenger.Name.Trim(),
                PassengerAge = int.Parse(passenger.Age.Trim(), CultureInfo.InvariantCulture),
                PassengerGender = passenger.Gender,
            });
        }

        return new CreateBookingRequest
        {
            TripId = command.TripId,
            ContactName = command.ContactName.Trim(),
            Seats = seats,
            ExpectedTotal = expectedTotal,
        };
    }

    private async Task<string> GetContactNameAsync(CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new HttpRequestMessage(HttpMethod.Get, "me");
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        ApiResult<ProfileName> profile = await ReadProfileAsync(response, cancellationToken);
        if (!profile.IsSuccess)
        {
            return string.Empty;
        }

        return profile.Value!.FullName;
    }

    // An answer that is no usable problem (a bare server error) is the same as no profile here.
    private static async Task<ApiResult<ProfileName>> ReadProfileAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.ReadApiResultAsync<ProfileName>(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ApiResult<ProfileName>.Invalid(new Dictionary<string, string[]>());
        }
    }
}
