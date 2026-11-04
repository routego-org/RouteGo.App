using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Bookings.BookSeats;

/// <summary>One bookable seat's passenger box: the seat, its price as shown, whether it is chosen, and what was typed.</summary>
public sealed record PassengerRow(BookableSeat Seat, int Fare, bool IsChosen, PassengerInput Input);

public sealed class BookSeatsModel : PageModel
{
    private const string HomeAddress = "/";

    private readonly BookSeatsHandler _handler;
    private string _contactName = string.Empty;

    public BookSeatsModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new BookSeatsHandler(httpClientFactory);
    }

    /// <summary>The trip named in the address, as typed: an address that names no trip is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? TripId { get; set; }

    /// <summary>The route label the seat map link passed ("Mumbai → Pune"); only shown in the heading.</summary>
    [BindProperty(SupportsGet = true, Name = "route")]
    public string? RouteName { get; set; }

    /// <summary>The ticked seats.</summary>
    [BindProperty]
    public List<string> Picks { get; set; } = new List<string>();

    /// <summary>What was typed for each chosen seat, by seat number.</summary>
    [BindProperty]
    public Dictionary<string, PassengerInput> Passengers { get; set; } = new Dictionary<string, PassengerInput>();

    /// <summary>The contact name as typed; an empty box arrives as null and is kept as nothing typed.</summary>
    [BindProperty]
    public string ContactName
    {
        get { return _contactName; }
        set { _contactName = value ?? string.Empty; }
    }

    /// <summary>The loaded seats; null when they could not be loaded.</summary>
    public BookableMap? Map { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Field errors by field key; shown under the box they name.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; private set; } = new Dictionary<string, string>();

    private IReadOnlyDictionary<string, int> _newFares = new Dictionary<string, int>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Guid? tripId = ParseTrip();
        if (tripId is null)
        {
            Alert = UnavailableAlert();
            return;
        }

        BookSeatsLoad load = await _handler.LoadAsync(tripId.Value, cancellationToken);
        ContactName = load.ContactName;
        ShowMap(load.Map);
    }

    /// <summary>"Pay ₹1,600": checks the form, asks Booking, and sends the browser on to the payment page.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Guid? tripId = ParseTrip();
        List<string> seats = BookSeatsChecks.Distinct(Picks);
        if (tripId is null || seats.Count > BookingDisplay.MaxSeats)
        {
            await ShowFormAgainAsync(cancellationToken);
            TellTooMany(seats);
            return Page();
        }

        var command = new PayCommand
        {
            TripId = tripId.Value,
            Seats = seats,
            Passengers = Passengers,
            ContactName = ContactName,
            SameAddress = PageAddress(),
            CorrelationId = CorrelationId.Get(HttpContext),
        };
        PayOutcome outcome = await _handler.PayAsync(command, cancellationToken);
        if (outcome.RedirectTo != null)
        {
            return Redirect(outcome.RedirectTo);
        }

        Picks = seats;
        await ShowFormAgainAsync(cancellationToken);
        ShowOutcome(outcome);
        return Page();
    }

    /// <summary>"Update seats": without a page script this is how a ticked seat gets its passenger boxes and the total.</summary>
    public async Task OnPostChooseAsync(CancellationToken cancellationToken)
    {
        List<string> seats = BookSeatsChecks.Distinct(Picks);
        Picks = seats.Take(BookingDisplay.MaxSeats).ToList();
        await ShowFormAgainAsync(cancellationToken);
        TellTooMany(seats);
    }

    /// <summary>"Book seats: Mumbai → Pune, 30 Oct 2026, 9:00 PM IST".</summary>
    public string Heading()
    {
        string departure = IndiaTime.Format(Map!.DepartureAt);
        if (string.IsNullOrWhiteSpace(RouteName))
        {
            return $"Book seats: trip leaving {departure}";
        }

        return $"Book seats: {RouteName.Trim()}, {departure}";
    }

    /// <summary>Whether seats can be chosen: the trip is neither cancelled nor departed.</summary>
    public bool CanBook()
    {
        return Map != null && !Map.IsCancelled && !Map.IsDeparted;
    }

    /// <summary>"1 of 3 seats available."</summary>
    public string AvailableText()
    {
        int available = Map!.Seats.Count(seat => seat.Status == SeatDisplay.Available);
        return $"{available} of {Map.Seats.Count} seats available.";
    }

    /// <summary>The seats to draw; the Available ones carry a tick box while the trip can be booked.</summary>
    public IReadOnlyList<SeatDisplay.DeckLayout> Grid()
    {
        var seats = new List<SeatDisplay.Seat>();
        foreach (BookableSeat seat in Map!.Seats)
        {
            bool canPick = CanBook() && seat.Status == SeatDisplay.Available;
            string? pickField = null;
            if (canPick)
            {
                pickField = nameof(Picks);
            }

            seats.Add(new SeatDisplay.Seat
            {
                SeatNo = seat.SeatNo,
                Deck = seat.Deck,
                Row = seat.Row,
                Column = seat.Column,
                SeatClass = seat.SeatClass,
                Fare = FareOf(seat),
                Status = seat.Status,
                PickField = pickField,
                IsPicked = canPick && Picks.Contains(seat.SeatNo),
            });
        }

        return SeatDisplay.ToGrid(seats);
    }

    /// <summary>A passenger box for every Available seat; the chosen ones are open, the others wait for the script to open them.</summary>
    public IReadOnlyList<PassengerRow> PassengerRows()
    {
        var rows = new List<PassengerRow>();
        foreach (BookableSeat seat in Map!.Seats.Where(seat => seat.Status == SeatDisplay.Available))
        {
            PassengerInput input = BookSeatsChecks.PassengerOf(Passengers, seat.SeatNo);
            rows.Add(new PassengerRow(seat, FareOf(seat), Picks.Contains(seat.SeatNo), input));
        }

        return rows;
    }

    /// <summary>The sum of the chosen seats' fares as shown, in whole rupees.</summary>
    public int Total()
    {
        return Map!.Seats.Where(seat => Picks.Contains(seat.SeatNo) && seat.Status == SeatDisplay.Available).Sum(FareOf);
    }

    /// <summary>"Total ₹1,600", or "Total ₹0" with nothing chosen.</summary>
    public string TotalText()
    {
        return $"Total {TripDisplay.Rupees(Total())}";
    }

    /// <summary>"Pay ₹1,600", or just "Pay" with no seat chosen.</summary>
    public string PayText()
    {
        if (Picks.Count == 0)
        {
            return "Pay";
        }

        return $"Pay {TripDisplay.Rupees(Total())}";
    }

    /// <summary>The error of a field, or null.</summary>
    public string? FieldError(string key)
    {
        if (FieldErrors.TryGetValue(key, out string? error))
        {
            return error;
        }

        return null;
    }

    /// <summary>Where the form's buttons post, keeping the route label.</summary>
    public string PostAddress(string? handler)
    {
        string address = PageAddress();
        if (handler is null)
        {
            return address;
        }

        return QueryHelpers.AddQueryString(address, "handler", handler);
    }

    private int FareOf(BookableSeat seat)
    {
        if (_newFares.TryGetValue(seat.SeatNo, out int fare))
        {
            return fare;
        }

        return seat.Fare;
    }

    private Guid? ParseTrip()
    {
        if (!Guid.TryParse(TripId, out Guid tripId))
        {
            return null;
        }

        return tripId;
    }

    private async Task ShowFormAgainAsync(CancellationToken cancellationToken)
    {
        Guid? tripId = ParseTrip();
        if (tripId is null)
        {
            Alert = UnavailableAlert();
            return;
        }

        ShowMap(await _handler.GetSeatMapAsync(tripId.Value, cancellationToken));
    }

    private void ShowMap(ApiResult<BookableMap> api)
    {
        if (api.IsSuccess)
        {
            Map = api.Value;
            return;
        }

        if (SeatDisplay.IsUnavailable(api))
        {
            Alert = UnavailableAlert();
            return;
        }

        Alert = BookingProblems.ToLoadAlert(api, CorrelationId.Get(HttpContext));
    }

    // After a refusal: the fares Booking named, then only seats still free stay chosen.
    private void ShowOutcome(PayOutcome outcome)
    {
        _newFares = outcome.NewFares;
        FieldErrors = outcome.FieldErrors;
        if (Map != null)
        {
            var free = Map.Seats.Where(seat => seat.Status == SeatDisplay.Available).Select(seat => seat.SeatNo).ToList();
            Picks = Picks.Where(seatNo => free.Contains(seatNo) && !outcome.Unpicked.Contains(seatNo)).ToList();
        }

        if (Alert is null)
        {
            Alert = outcome.Alert;
        }

        if (Alert is null && FieldErrors.TryGetValue(BookSeatsChecks.PageKey, out string? pageError))
        {
            Alert = PageAlert.Danger(pageError);
        }
    }

    private void TellTooMany(List<string> seats)
    {
        if (seats.Count > BookingDisplay.MaxSeats && Alert is null)
        {
            Alert = PageAlert.Danger(BookSeatsChecks.TooManySeats);
        }
    }

    private PageAlert UnavailableAlert()
    {
        return SeatDisplay.UnavailableAlert(PageAddress(), Messages.AccessGoHome, HomeAddress);
    }

    // The page's own address as typed, with the route label passed on: what "Try again" and the form post go to.
    private string PageAddress()
    {
        string trip = Uri.EscapeDataString(TripId ?? string.Empty);
        return SeatDisplay.WithRoute($"/trips/{trip}/book", RouteName);
    }
}
