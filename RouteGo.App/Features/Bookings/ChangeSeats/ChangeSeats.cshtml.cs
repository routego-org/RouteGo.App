namespace RouteGo.App.Features.Bookings.ChangeSeats;

public sealed class ChangeSeatsModel : PageModel
{
    private const string MovesKey = "ChangedMoves";

    private readonly ChangeSeatsHandler _handler;
    private readonly IClock _clock;

    public ChangeSeatsModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new ChangeSeatsHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The booking named in the address, as typed: an address that is no identifier is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? BookingId { get; set; }

    /// <summary>The new seat picked for each of the booking's seats, by the booking's seat number; empty means "keep this seat".</summary>
    [BindProperty]
    public Dictionary<string, string> Moves { get; set; } = new Dictionary<string, string>();

    /// <summary>Set after a change went through: the page shows what was moved.</summary>
    [BindProperty(SupportsGet = true, Name = "changed")]
    public bool IsChanged { get; set; }

    public ChangePageView? View { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>"4C → 5C, 4D → 5D" after a change went through; null otherwise.</summary>
    public string? ChangedMoves { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (IsChanged && TryTakeMoves())
        {
            return;
        }

        await ShowChoicesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostChangeAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingProblems.NotFoundAlert();
            return Page();
        }

        List<SeatMove> moves = ChangeSeatsChoices.MovesFrom(Moves);
        string? checkProblem = ChangeSeatsChoices.Check(moves);
        if (checkProblem != null)
        {
            await ShowChoicesAsync(cancellationToken);
            Alert = Alert ?? PageAlert.Danger(checkProblem);
            return Page();
        }

        ChangeOutcome outcome = await _handler.ChangeAsync(bookingId.Value, moves, CorrelationId.Get(HttpContext), cancellationToken);
        if (outcome.MovesText != null)
        {
            TempData[MovesKey] = outcome.MovesText;
            return RedirectToPage(new { bookingId = BookingId, changed = true });
        }

        if (outcome.ShouldReload)
        {
            await ShowChoicesAsync(cancellationToken);
        }

        // The refusal says what happened and that nothing changed; it wins over any state the reload found.
        Alert = outcome.Alert;
        return Page();
    }

    /// <summary>"Change seats: Mumbai → Pune, 30 Oct 2026, 9:00 PM IST", or "Change seats" when no booking is shown.</summary>
    public string Heading()
    {
        if (View is null || View.Booking is null)
        {
            return "Change seats";
        }

        return $"Change seats: {BookingDisplay.RouteAndDeparture(View.Booking.Trip)}";
    }

    public string BookingAddress()
    {
        return $"/my-bookings/{BookingId}";
    }

    public string TicketAddress()
    {
        return $"/tickets/{BookingId}";
    }

    public string ChangeAddress()
    {
        return $"{BookingAddress()}/change-seats?handler=Change";
    }

    /// <summary>"Your seats were changed: 4C → 5C. Your ticket is being updated and will be sent to you again."</summary>
    public string SuccessText()
    {
        return $"Your seats were changed: {ChangedMoves}. Your ticket is being updated and will be sent to you again.";
    }

    private async Task ShowChoicesAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingProblems.NotFoundAlert();
            return;
        }

        var query = new ChangeSeatsQuery
        {
            BookingId = bookingId.Value,
            Now = _clock.UtcNow,
            CorrelationId = CorrelationId.Get(HttpContext),
            Picked = Moves,
        };
        View = await _handler.ViewAsync(query, cancellationToken);
        Alert = View.Alert;
    }

    // The moves survive one redirect only; a refreshed page falls back to the normal load.
    private bool TryTakeMoves()
    {
        if (BookingDisplay.ParseBookingId(BookingId) is null || TempData[MovesKey] is not string moves)
        {
            return false;
        }

        ChangedMoves = moves;
        return true;
    }
}
