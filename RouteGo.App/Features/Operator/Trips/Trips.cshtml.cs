namespace RouteGo.App.Features.Operator.Trips;

public sealed class TripsModel : PageModel
{
    public const string AllTripsAddress = "/operator/trips";

    private const string TripsPage = "/Operator/Trips/Trips";
    private const string Items = "trips";
    private const string Entity = "trip";

    private readonly TripsHandler _handler;
    private readonly IClock _clock;

    public TripsModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new TripsHandler(httpClientFactory);
        _clock = clock;
    }

    [BindProperty(SupportsGet = true)]
    public TripsQuery Query { get; set; } = new TripsQuery();

    /// <summary>Set by a trip's "Change fares" link: the trip whose fare form is open.</summary>
    [BindProperty(SupportsGet = true, Name = "fares")]
    public Guid? FaresTripId { get; set; }

    /// <summary>Set by a trip's "Cancel trip" link: the trip whose cancellation is being confirmed.</summary>
    [BindProperty(SupportsGet = true, Name = "cancel")]
    public Guid? CancelTripId { get; set; }

    [BindProperty]
    public TripFaresInput FaresInput { get; set; } = new TripFaresInput();

    [BindProperty]
    public TripCancelInput CancelInput { get; set; } = new TripCancelInput();

    /// <summary>The travel dates listed; null when the typed range is not allowed.</summary>
    public TripDates? Dates { get; private set; }

    /// <summary>The loaded list; null when it could not be loaded.</summary>
    public TripsPage? Trips { get; private set; }

    /// <summary>The operator's buses for the Bus filter; null when Fleet would not give them, and then the filter is not offered.</summary>
    public IReadOnlyList<TripBus>? Buses { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the page shows only the alert: Fleet will not list this operator's trips yet or at all.</summary>
    public bool IsContentHidden { get; private set; }

    /// <summary>The trip whose fare form is shown; null when none is.</summary>
    public TripDetails? FareTrip { get; private set; }

    /// <summary>The trip whose cancellation is being confirmed; null when none is.</summary>
    public TripDetails? CancelTrip { get; private set; }

    /// <summary>The moment the actions are decided by.</summary>
    public DateTimeOffset Now()
    {
        return _clock.UtcNow;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await ShowListAsync(cancellationToken);
        TripDetails? fareTrip = FindTrip(FaresTripId);
        if (fareTrip != null && TripActions.CanChangeFares(fareTrip, Now()))
        {
            FareTrip = fareTrip;
            FaresInput = new TripFaresInput { TripId = fareTrip.TripId, Fares = ScheduleFormInput.FaresFrom(fareTrip.ClassFares) };
        }

        TripDetails? cancelTrip = FindTrip(CancelTripId);
        if (cancelTrip != null && cancelTrip.IsScheduled)
        {
            CancelTrip = cancelTrip;
            CancelInput = new TripCancelInput { TripId = cancelTrip.TripId };
        }
    }

    public async Task<IActionResult> OnPostFaresAsync(CancellationToken cancellationToken)
    {
        TripDetails? trip = await ShowListWithTripAsync(FaresInput.TripId, cancellationToken);
        if (trip is null)
        {
            return Page();
        }

        bool canSend = await CheckFaresAsync(trip, cancellationToken);
        if (!canSend)
        {
            return Page();
        }

        ApiResult<TripDetails> api = await _handler.ReplaceTripFaresAsync(trip.TripId, FaresInput.ClassFares(), cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = "Fares changed. New bookings pay the new fares.";
            return RedirectToPage(TripsPage, ListValues(Query.PageNumber));
        }

        FareTrip = trip;
        await ShowRefusalAsync(api, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        TripDetails? trip = await ShowListWithTripAsync(CancelInput.TripId, cancellationToken);
        if (trip is null)
        {
            return Page();
        }

        ValidationResult validation = await new TripCancelValidator().ValidateAsync(CancelInput, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(CancelInput));
            CancelTrip = trip;
            return Page();
        }

        ApiResult<TripDetails> api = await _handler.CancelTripAsync(trip.TripId, CancelInput.Reason!.Trim(), cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = "Trip cancelled.";
            return RedirectToPage(TripsPage, ListValues(Query.PageNumber));
        }

        CancelTrip = trip;
        await ShowRefusalAsync(api, cancellationToken);
        return Page();
    }

    /// <summary>The list address with the current filters and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(TripsPage, ListValues(page))!;
    }

    /// <summary>The list address with the trip's fare form open.</summary>
    public string FaresAddress(Guid tripId)
    {
        RouteValueDictionary values = ListValues(Query.PageNumber);
        values["fares"] = tripId;
        return Url.Page(TripsPage, values)!;
    }

    /// <summary>The list address with the trip's cancellation question open.</summary>
    public string CancelAddress(Guid tripId)
    {
        RouteValueDictionary values = ListValues(Query.PageNumber);
        values["cancel"] = tripId;
        return Url.Page(TripsPage, values)!;
    }

    /// <summary>Where a form posts: the named step, with the list's filters, so the same list is shown afterwards.</summary>
    public string PostAddress(string handler)
    {
        return Url.Page(TripsPage, handler, ListValues(Query.PageNumber))!;
    }

    private RouteValueDictionary ListValues(int page)
    {
        return new RouteValueDictionary
        {
            ["from"] = Query.From,
            ["to"] = Query.To,
            ["status"] = Query.Status,
            ["busId"] = Query.BusId,
            ["scheduleId"] = Query.ScheduleId,
            ["pageNumber"] = page,
        };
    }

    private TripDetails? FindTrip(Guid? tripId)
    {
        if (tripId is null || Trips is null)
        {
            return null;
        }

        return Trips.Items.FirstOrDefault(trip => trip.TripId == tripId.Value);
    }

    // The buses for the filter and, when the dates are allowed, the trips; a refused filter in the
    // address shows the whole default list instead, with why.
    private async Task ShowListAsync(CancellationToken cancellationToken)
    {
        Task<ApiResult<TripBusList>> buses = _handler.ListBusesAsync(cancellationToken);
        Dates = TripDisplay.ListDates(Query.From, Query.To, IndiaTime.Today(Now()));
        if (Dates != null)
        {
            ApiResult<TripsPage> api = await _handler.ListTripsAsync(Query, Dates, cancellationToken);
            if (api.Errors != null && !TripRefusals.NamesDates(api.Errors))
            {
                Alert = PageAlert.Danger(api.ErrorsTitle!);
                Query = new TripsQuery();
                Dates = TripDisplay.ListDates(Query.From, Query.To, IndiaTime.Today(Now()));
                api = await _handler.ListTripsAsync(Query, Dates!, cancellationToken);
            }

            ShowTrips(api);
        }

        Buses = (await buses).Value?.Items;
    }

    // The list as it is now, and the posted trip on it; null (told as not found) when the trip is no longer listed.
    private async Task<TripDetails?> ShowListWithTripAsync(Guid tripId, CancellationToken cancellationToken)
    {
        await ShowListAsync(cancellationToken);
        TripDetails? trip = FindTrip(tripId);
        if (trip is null && Alert is null)
        {
            Alert = PageAlert.Danger(Messages.ProblemNotFound(Entity));
        }

        return trip;
    }

    private void ShowTrips(ApiResult<TripsPage> api)
    {
        if (api.IsSuccess)
        {
            Trips = api.Value;
            return;
        }

        if (api.Errors != null)
        {
            Dates = null;
            return;
        }

        Alert = FleetDisplay.ToAlert(api.Problem!, Items, CorrelationId.Get(HttpContext));
        IsContentHidden = FleetDisplay.HidesRecord(api.Problem!);
    }

    // A refusal about the trip's state is told, the form closed and the list read again; Fleet down
    // or failing keeps the open form with what was typed.
    private async Task ShowRefusalAsync(ApiResult<TripDetails> api, CancellationToken cancellationToken)
    {
        if (api.Errors != null)
        {
            bool isPlaced = TripRefusals.PlaceFieldErrors(api.Errors, FareTrip != null, FaresInput.Fares.Count, ModelState);
            if (!isPlaced)
            {
                Alert = PageAlert.Danger(api.ErrorsTitle ?? Messages.ProblemInternalError(CorrelationId.Get(HttpContext)));
            }

            return;
        }

        ApiProblem problem = api.Problem!;
        PageAlert? stateAlert = TripRefusals.TripStateAlert(problem);
        if (stateAlert != null)
        {
            FareTrip = null;
            CancelTrip = null;
            await ShowListAsync(cancellationToken);
            Alert = stateAlert;
            return;
        }

        Alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
        IsContentHidden = problem.Code == FleetDisplay.OperatorProfileNotReady;
    }

    // False when nothing may be sent: a fare is not allowed (the form stays open), or no fare changed.
    private async Task<bool> CheckFaresAsync(TripDetails trip, CancellationToken cancellationToken)
    {
        ValidationResult validation = await new TripFaresValidator().ValidateAsync(FaresInput, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(FaresInput));
            FareTrip = trip;
            return false;
        }

        if (TripActions.IsUnchanged(FaresInput, trip))
        {
            Alert = PageAlert.Info("The fares are the same as before. Nothing was changed.");
            return false;
        }

        return true;
    }
}
