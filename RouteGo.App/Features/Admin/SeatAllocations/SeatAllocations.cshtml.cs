namespace RouteGo.App.Features.Admin.SeatAllocations;

public sealed class SeatAllocationsModel : PageModel
{
    public const string TripsAddress = "/admin/trips";
    public const string BackToTrips = "Back to trips";

    private const string SeatAllocationsPage = "/Admin/SeatAllocations/SeatAllocations";
    private const string NoteField = "note";

    private readonly SeatAllocationsHandler _handler;
    private readonly IClock _clock;

    public SeatAllocationsModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new SeatAllocationsHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The trip named in the address, as typed: an address that names no trip is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? TripId { get; set; }

    /// <summary>The route label the "Seats" link passed ("Mumbai → Pune"); only shown in the heading.</summary>
    [BindProperty(SupportsGet = true, Name = "route")]
    public string? RouteName { get; set; }

    /// <summary>Set by a stuck hold's "Cancel hold" link: the booking whose cancellation is being confirmed.</summary>
    [BindProperty(SupportsGet = true, Name = "cancel")]
    public Guid? CancelBookingId { get; set; }

    [BindProperty]
    public HoldCancelInput CancelInput { get; set; } = new HoldCancelInput();

    /// <summary>The loaded seats; null when they could not be loaded.</summary>
    public StaffSeatList? Seats { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>The stuck holds on the trip as loaded; empty when there are none.</summary>
    public IReadOnlyList<StuckHold> Stuck { get; private set; } = Array.Empty<StuckHold>();

    /// <summary>The stuck hold whose cancellation is being confirmed; null when no question is open.</summary>
    public StuckHold? CancelHold { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await ShowSeatsAsync(cancellationToken);
        OpenQuestion(CancelBookingId);
        if (CancelHold != null)
        {
            CancelInput = new HoldCancelInput { BookingId = CancelHold.BookingId };
        }
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        await ShowSeatsAsync(cancellationToken);
        if (Seats is null)
        {
            return Page();
        }

        ValidationResult validation = await new HoldCancelValidator().ValidateAsync(CancelInput, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(CancelInput));
            OpenQuestion(CancelInput.BookingId);
            return Page();
        }

        string reason = CancelInput.Reason!.Trim();
        ApiResult<CancelledHold> api = await _handler.CancelHoldAsync(Seats.TripId, CancelInput.BookingId, reason, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = StuckHolds.Cancelled(api.Value!.ReleasedSeatNos, Seats);
            return RedirectToPage(SeatAllocationsPage, SeatsValues());
        }

        await ShowRefusalAsync(api, cancellationToken);
        return Page();
    }

    /// <summary>"Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST".</summary>
    public string Heading()
    {
        return SeatDisplay.Heading(RouteName, Seats!.DepartureAt);
    }

    /// <summary>"Available: 1 · On hold: 4 · Booked: 1 · Blocked: 1".</summary>
    public string CountsText()
    {
        var statuses = Seats!.Items.Select(seat => seat.Status).ToList();
        return SeatDisplay.CountsText(statuses);
    }

    /// <summary>The trip's public seat map, with the route label passed on.</summary>
    public string PublicSeatMapAddress()
    {
        return SeatDisplay.PublicSeatMapAddress(Seats!.TripId, RouteName);
    }

    /// <summary>The page address, with the stuck hold's cancellation question open.</summary>
    public string CancelAddress(Guid bookingId)
    {
        RouteValueDictionary values = SeatsValues();
        values["cancel"] = bookingId;
        return Url.Page(SeatAllocationsPage, values)!;
    }

    /// <summary>The page address with no question open: where "Go back" leads.</summary>
    public string SeatsAddress()
    {
        return Url.Page(SeatAllocationsPage, SeatsValues())!;
    }

    /// <summary>Where the cancel question posts, keeping the route label.</summary>
    public string PostAddress()
    {
        return Url.Page(SeatAllocationsPage, "Cancel", SeatsValues())!;
    }

    /// <summary>The seats to draw, with booking IDs, hold end times, block reasons and stuck marks; none can be picked.</summary>
    public IReadOnlyList<SeatDisplay.DeckLayout> Grid()
    {
        DateTimeOffset now = _clock.UtcNow;
        var seats = new List<SeatDisplay.Seat>();
        foreach (StaffSeat seat in Seats!.Items)
        {
            seats.Add(ToSeat(seat, StuckHolds.IsStuck(seat, now)));
        }

        return SeatDisplay.ToGrid(seats);
    }

    private async Task ShowSeatsAsync(CancellationToken cancellationToken)
    {
        Seats = null;
        Stuck = Array.Empty<StuckHold>();
        if (!Guid.TryParse(TripId, out Guid tripId))
        {
            Alert = UnavailableAlert();
            return;
        }

        ApiResult<StaffSeatList> api = await _handler.ListSeatsAsync(tripId, cancellationToken);
        if (api.IsSuccess)
        {
            Seats = api.Value;
            Stuck = StuckHolds.Find(Seats!.Items, _clock.UtcNow);
            return;
        }

        if (SeatDisplay.IsUnavailable(api))
        {
            Alert = UnavailableAlert();
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }

    // The reason refused, or Seat Inventory out of reach: the question stays open with what was typed.
    // A trip Seat Inventory no longer knows, or one the admin may not see, shows no seats. Any other
    // refusal is told above the seats loaded again.
    private async Task ShowRefusalAsync(ApiResult<CancelledHold> api, CancellationToken cancellationToken)
    {
        string correlationId = CorrelationId.Get(HttpContext);
        if (NamesReason(api.Errors) || IsOutage(api.Problem))
        {
            ShowKeptQuestion(api.Problem, correlationId);
            return;
        }

        ModelState.Clear();
        string? code = api.Problem?.Code;
        if (code == SeatDisplay.NotFound)
        {
            Seats = null;
            Alert = UnavailableAlert();
            return;
        }

        if (code == SharedProblems.Forbidden)
        {
            Seats = null;
            Alert = PageAlert.Danger(Messages.ProblemForbidden);
            return;
        }

        await ShowSeatsAsync(cancellationToken);
        if (Seats != null)
        {
            Alert = RefusalAlert(api, CancelInput.BookingId, correlationId);
        }
    }

    private void ShowKeptQuestion(ApiProblem? outage, string correlationId)
    {
        if (outage is null)
        {
            ModelState.AddModelError($"{nameof(CancelInput)}.{nameof(HoldCancelInput.Reason)}", Messages.ValidationReasonLength);
        }
        else
        {
            Alert = SharedProblems.ToAlert(outage, correlationId);
        }

        OpenQuestion(CancelInput.BookingId);
    }

    private static bool NamesReason(IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null)
        {
            return false;
        }

        return errors.Keys.Any(field => field.Equals(NoteField, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOutage(ApiProblem? problem)
    {
        if (problem is null)
        {
            return false;
        }

        return problem.Code == SharedProblems.UpstreamUnavailable || problem.Code == SharedProblems.UpstreamTimeout;
    }

    private static PageAlert RefusalAlert(ApiResult<CancelledHold> api, Guid bookingId, string correlationId)
    {
        if (api.Errors != null)
        {
            return PageAlert.Danger(api.ErrorsTitle ?? Messages.ProblemInternalError(correlationId));
        }

        PageAlert? holdAlert = StuckHolds.HoldStateAlert(api.Problem!, bookingId);
        if (holdAlert != null)
        {
            return holdAlert;
        }

        return SharedProblems.ToAlert(api.Problem!, correlationId);
    }

    private void OpenQuestion(Guid? bookingId)
    {
        CancelHold = Stuck.FirstOrDefault(hold => hold.BookingId == bookingId);
    }

    private PageAlert UnavailableAlert()
    {
        return SeatDisplay.UnavailableAlert(SeatsAddress(), BackToTrips, TripsAddress);
    }

    private RouteValueDictionary SeatsValues()
    {
        return new RouteValueDictionary { ["tripId"] = TripId, ["route"] = RouteName };
    }

    private static SeatDisplay.Seat ToSeat(StaffSeat seat, bool isStuck)
    {
        var lines = new List<SeatDisplay.SeatLine>();
        if (seat.BookingId != null)
        {
            lines.Add(new SeatDisplay.SeatLine("Booking ID", seat.BookingId, $"/admin/bookings/{Uri.EscapeDataString(seat.BookingId)}"));
        }

        if (seat.Status == SeatDisplay.Held && seat.HoldExpiresAt != null)
        {
            lines.Add(new SeatDisplay.SeatLine("Hold ends at", IndiaTime.Format(seat.HoldExpiresAt.Value)));
        }

        if (seat.Status == SeatDisplay.Blocked && seat.BlockReason != null)
        {
            lines.Add(SeatDisplay.BlockedByLine(seat.BlockReason));
        }

        string? warning = null;
        if (isStuck)
        {
            warning = $"Stuck: hold time ended at {IndiaTime.Format(seat.HoldExpiresAt!.Value)}.";
        }

        return new SeatDisplay.Seat
        {
            SeatNo = seat.SeatNo,
            Deck = seat.Deck,
            Row = seat.Row,
            Column = seat.Column,
            SeatClass = seat.SeatClass,
            Fare = seat.Fare,
            Status = seat.Status,
            Lines = lines,
            Warning = warning,
        };
    }
}
