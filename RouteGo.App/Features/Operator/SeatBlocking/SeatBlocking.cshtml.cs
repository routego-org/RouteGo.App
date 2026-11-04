namespace RouteGo.App.Features.Operator.SeatBlocking;

public sealed class SeatBlockingModel : PageModel
{
    public const string MyTripsAddress = "/operator/trips";
    public const string BackToMyTrips = "Back to my trips";

    /// <summary>The field a ticked seat is posted as when it was Available as shown.</summary>
    public const string AvailableField = "AvailablePicks";

    /// <summary>The field a ticked seat is posted as when the operator had blocked it as shown.</summary>
    public const string BlockedField = "BlockedPicks";

    private const string SeatBlockingPage = "/Operator/SeatBlocking/SeatBlocking";

    private readonly SeatBlockingHandler _handler;

    public SeatBlockingModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new SeatBlockingHandler(httpClientFactory);
    }

    /// <summary>The trip named in the address, as typed: an address that names no trip is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? TripId { get; set; }

    /// <summary>The route label the "Seats" link passed ("Mumbai → Pune"); only shown in the heading.</summary>
    [BindProperty(SupportsGet = true, Name = "route")]
    public string? RouteName { get; set; }

    /// <summary>The ticked seats that were Available when the page was shown: what "Block seats" sends.</summary>
    [BindProperty]
    public List<string> AvailablePicks { get; set; } = new List<string>();

    /// <summary>The ticked seats the operator had blocked when the page was shown: what "Unblock seats" sends.</summary>
    [BindProperty]
    public List<string> BlockedPicks { get; set; } = new List<string>();

    /// <summary>The seats the unblock question named, sent when the operator confirms.</summary>
    [BindProperty]
    public List<string> ConfirmSeatNos { get; set; } = new List<string>();

    /// <summary>Set by the question's "Unblock seats": the operator has confirmed.</summary>
    [BindProperty]
    public bool Confirmed { get; set; }

    /// <summary>The loaded seats; null when they could not be loaded.</summary>
    public TripSeatList? Seats { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>The seats the unblock question names; null when no question is open.</summary>
    public IReadOnlyList<string>? UnblockSeatNos { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await ShowSeatsAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostBlockAsync(CancellationToken cancellationToken)
    {
        List<string> seatNos = SeatPicks.Distinct(AvailablePicks);
        string? pickProblem = SeatPicks.Check(seatNos, SeatPicks.NothingToBlock);
        bool isTrip = Guid.TryParse(TripId, out Guid tripId);
        if (pickProblem != null || !isTrip)
        {
            await ShowSeatsAsync(cancellationToken);
            ShowPickProblem(pickProblem);
            return Page();
        }

        ApiResult<SeatBlock> api = await _handler.BlockSeatsAsync(tripId, seatNos, cancellationToken);
        if (!api.IsSuccess)
        {
            await ShowRefusalAsync(api, cancellationToken);
            return Page();
        }

        SeatBlock block = api.Value!;
        if (block.BlockedSeatNos.Count == 0)
        {
            TempData[Flash.Key] = "These seats were already blocked. Nothing was changed.";
            TempData[Flash.KindKey] = Flash.KindInfo;
        }
        else
        {
            TempData[Flash.Key] = $"Seats blocked: {SeatDisplay.SeatList(block.SeatNos)}. Customers cannot book them.";
        }

        return RedirectToPage(SeatBlockingPage, SeatsValues());
    }

    public async Task<IActionResult> OnPostUnblockAsync(CancellationToken cancellationToken)
    {
        List<string> seatNos = SeatPicks.Distinct(BlockedPicks);
        if (Confirmed)
        {
            seatNos = SeatPicks.Distinct(ConfirmSeatNos);
        }

        string? pickProblem = SeatPicks.Check(seatNos, SeatPicks.NothingToUnblock);
        bool isTrip = Guid.TryParse(TripId, out Guid tripId);
        if (pickProblem != null || !isTrip || !Confirmed)
        {
            await ShowSeatsAsync(cancellationToken);
            AskOrShowPickProblem(pickProblem, seatNos);
            return Page();
        }

        ApiResult<bool> api = await _handler.UnblockSeatsAsync(tripId, seatNos, cancellationToken);
        if (!api.IsSuccess)
        {
            await ShowRefusalAsync(api, cancellationToken);
            return Page();
        }

        TempData[Flash.Key] = $"Seats unblocked: {SeatDisplay.SeatList(seatNos)}. They are back on sale.";
        return RedirectToPage(SeatBlockingPage, SeatsValues());
    }

    /// <summary>"Go back" from the unblock question: nothing is sent and the picked seats stay picked.</summary>
    public async Task OnPostBackAsync(CancellationToken cancellationToken)
    {
        await ShowSeatsAsync(cancellationToken);
    }

    /// <summary>"Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST".</summary>
    public string Heading()
    {
        return SeatDisplay.Heading(RouteName, Seats!.DepartureAt);
    }

    /// <summary>Whether seats can still be blocked or unblocked: the trip is neither cancelled nor departed.</summary>
    public bool CanAct()
    {
        return !Seats!.IsCancelled && !Seats.IsDeparted;
    }

    /// <summary>"Available: 2 · On hold: 1 · Booked: 1 · Blocked: 1".</summary>
    public string CountsText()
    {
        var statuses = Seats!.Items.Select(seat => seat.Status).ToList();
        return SeatDisplay.CountsText(statuses);
    }

    /// <summary>"Put L2 back on sale? Customers can book it straight away."</summary>
    public string UnblockQuestion()
    {
        string pronoun = UnblockSeatNos!.Count == 1 ? "it" : "them";
        return $"Put {SeatDisplay.SeatList(UnblockSeatNos)} back on sale? Customers can book {pronoun} straight away.";
    }

    /// <summary>Where the seat form's buttons post: the named step, keeping the route label.</summary>
    public string PostAddress(string handler)
    {
        return Url.Page(SeatBlockingPage, handler, SeatsValues())!;
    }

    /// <summary>The trip's public seat map, with the route label passed on.</summary>
    public string PublicSeatMapAddress()
    {
        return SeatDisplay.PublicSeatMapAddress(Seats!.TripId, RouteName);
    }

    /// <summary>The seats to draw: Available seats and the operator's own blocks can be picked while the trip can still change.</summary>
    public IReadOnlyList<SeatDisplay.DeckLayout> Grid()
    {
        bool canAct = CanAct();
        var seats = new List<SeatDisplay.Seat>();
        foreach (TripSeat seat in Seats!.Items)
        {
            string? pickField = TripSeatView.PickField(seat, canAct);
            seats.Add(TripSeatView.ToSeat(seat, pickField, IsPicked(seat.SeatNo, pickField)));
        }

        return SeatDisplay.ToGrid(seats);
    }

    private async Task ShowSeatsAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(TripId, out Guid tripId))
        {
            Alert = UnavailableAlert();
            return;
        }

        ApiResult<TripSeatList> api = await _handler.ListSeatsAsync(tripId, cancellationToken);
        if (api.IsSuccess)
        {
            Seats = api.Value;
            return;
        }

        if (SeatDisplay.IsUnavailable(api))
        {
            Alert = UnavailableAlert();
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }

    // After a refused block or unblock nothing stays picked and the seats are loaded again; a trip
    // Seat Inventory no longer knows, or another company's, shows no seats at all.
    private async Task ShowRefusalAsync<T>(ApiResult<T> api, CancellationToken cancellationToken)
    {
        AvailablePicks.Clear();
        BlockedPicks.Clear();
        if (api.Problem != null && api.Problem.Code == SharedProblems.Forbidden)
        {
            Alert = PageAlert.Danger(Messages.ProblemForbidden);
            return;
        }

        if (api.Problem != null && api.Problem.Code == SeatDisplay.NotFound)
        {
            Alert = UnavailableAlert();
            return;
        }

        await ShowSeatsAsync(cancellationToken);
        if (Seats != null)
        {
            Alert = SeatRefusals.ToAlert(api, CorrelationId.Get(HttpContext));
        }
    }

    // A check on the picks is told only above seats that could be loaded; otherwise the load says why.
    private void ShowPickProblem(string? pickProblem)
    {
        if (Seats != null && pickProblem != null)
        {
            Alert = PageAlert.Danger(pickProblem);
        }
    }

    private void AskOrShowPickProblem(string? pickProblem, List<string> seatNos)
    {
        if (Seats != null && pickProblem is null)
        {
            UnblockSeatNos = seatNos;
            return;
        }

        ShowPickProblem(pickProblem);
    }

    private PageAlert UnavailableAlert()
    {
        string sameAddress = Url.Page(SeatBlockingPage, SeatsValues())!;
        return SeatDisplay.UnavailableAlert(sameAddress, BackToMyTrips, MyTripsAddress);
    }

    private RouteValueDictionary SeatsValues()
    {
        return new RouteValueDictionary { ["tripId"] = TripId, ["route"] = RouteName };
    }

    private bool IsPicked(string seatNo, string? pickField)
    {
        if (pickField == AvailableField)
        {
            return AvailablePicks.Contains(seatNo);
        }

        return pickField == BlockedField && BlockedPicks.Contains(seatNo);
    }
}
