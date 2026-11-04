namespace RouteGo.App.Features.Operator.EditBus;

/// <summary>One deck of the saved seat layout.</summary>
public sealed record SavedDeck(string Deck, int Rows, int Columns)
{
    public int? AisleAfterColumn { get; init; }
}

/// <summary>One seat of the saved seat layout.</summary>
public sealed record SavedSeat
{
    public required string Label { get; init; }

    public required string Deck { get; init; }

    public required int Row { get; init; }

    public required int Column { get; init; }

    public required string SeatClass { get; init; }
}

public sealed record SavedLayout(IReadOnlyList<SavedDeck> Decks, IReadOnlyList<SavedSeat> Seats);

/// <summary>The fields of one of the operator's buses the form is filled with.</summary>
public sealed record SavedBus
{
    public required Guid BusId { get; init; }

    public required string RegistrationNumber { get; init; }

    public required string Name { get; init; }

    public required string BusType { get; init; }

    public required bool IsAc { get; init; }

    public required IReadOnlyList<string> Amenities { get; init; }

    public required IReadOnlyList<string> SeatClasses { get; init; }

    public required SavedLayout Layout { get; init; }
}

public sealed class EditBusHandler
{
    private const string BusesPath = "buses";

    private readonly IHttpClientFactory _httpClientFactory;

    public EditBusHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The bus as saved, to fill in the form.</summary>
    public async Task<ApiResult<SavedBus>> GetBusAsync(Guid busId, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BusesPath}/{busId}");
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<SavedBus>(cancellationToken);
    }

    /// <summary>
    /// Replaces every detail of the bus. Fleet refuses a new bus type, seat classes or seats while
    /// the bus has upcoming trips.
    /// </summary>
    public async Task<ApiResult<SavedBus>> ReplaceBusAsync(Guid busId, BusRequest bus, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Put, $"{BusesPath}/{busId}")
        {
            Content = JsonContent.Create(bus, options: WireJson.Options),
        };
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<SavedBus>(cancellationToken);
    }
}

public sealed class EditBusModel : PageModel
{
    public const string BusListAddress = "/operator/buses";
    public const string BackToListText = "Back to my buses";
    public const string SeatsLockedText = "You can still change the name, registration number, AC and amenities. To change the bus type, seat classes or seats, the bus must have no upcoming trips.";

    private const string EditBusPage = "/Operator/EditBus/EditBus";
    private const string BusPage = "/Operator/Bus/Bus";
    private const string Entity = "bus";
    private const string EntityLabel = "Bus";
    private const string RegistrationTaken = "BUS_REGISTRATION_ALREADY_EXISTS";
    private const string BusInUse = "BUS_IN_USE";

    private readonly EditBusHandler _handler;

    public EditBusModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new EditBusHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public Guid BusId { get; set; }

    [BindProperty]
    public BusFormInput Input { get; set; } = new BusFormInput();

    /// <summary>The builder step a builder button posts ("fill:lower"); empty for "Save bus".</summary>
    [BindProperty]
    public string? BuilderAction { get; set; }

    /// <summary>The bus's name as saved, for the title while the name box is being changed.</summary>
    [BindProperty]
    public string? SavedName { get; set; }

    public BusFormOutcome Outcome { get; private set; } = new BusFormOutcome();

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the form is drawn; false when the bus could not be loaded or may not be shown.</summary>
    public bool ShowsForm { get; private set; } = true;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<SavedBus> api = await _handler.GetBusAsync(BusId, cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = AlertFor(api.Problem!);
            ShowsForm = false;
            return;
        }

        SavedName = api.Value!.Name;
        Input = ToForm(api.Value);
    }

    // A builder step only changes the form: the page is drawn again with what was typed, and
    // nothing goes to Fleet.
    public IActionResult OnPostBuild()
    {
        Outcome = BusFormBuilder.Apply(Input, BuilderAction);
        ShowOutcome();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Outcome = BusFormBuilder.PrepareSave(Input);
        ShowOutcome();
        if (Outcome.StopsSave)
        {
            return Page();
        }

        var validator = new BusFormValidator();
        ValidationResult validation = await validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return Page();
        }

        ApiResult<SavedBus> api = await _handler.ReplaceBusAsync(BusId, BusRequest.From(Input), cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = Messages.SuccessUpdated(EntityLabel);
            return RedirectToPage(BusPage, new { busId = BusId });
        }

        ShowRefusal(api);
        return Page();
    }

    /// <summary>"Change {bus name}"; null while no bus is shown.</summary>
    public string? Title()
    {
        if (string.IsNullOrEmpty(SavedName))
        {
            return null;
        }

        return "Change " + SavedName;
    }

    /// <summary>What the shared bus form partial draws.</summary>
    public BusFormView View()
    {
        return new BusFormView
        {
            Input = Input,
            Outcome = Outcome,
            SaveAddress = Url.Page(EditBusPage, new { busId = BusId })!,
            BuildAddress = Url.Page(EditBusPage, "Build", new { busId = BusId })!,
            CancelAddress = Url.Page(BusPage, new { busId = BusId })!,
            SavedName = SavedName,
        };
    }

    // Steps move seats and classes around, so the posted values are drawn from Input, not ModelState.
    private void ShowOutcome()
    {
        ModelState.Clear();
        foreach (BusFormProblem problem in Outcome.Problems)
        {
            ModelState.AddModelError(nameof(Input) + "." + problem.Field, problem.Message);
        }
    }

    private void ShowRefusal(ApiResult<SavedBus> api)
    {
        if (api.Errors != null)
        {
            foreach (BusFormProblem problem in BusFormFleetErrors.Place(api.Errors, Input))
            {
                if (problem.Field.Length == 0)
                {
                    Alert = PageAlert.Danger(api.ErrorsTitle ?? problem.Message);
                    continue;
                }

                ModelState.AddModelError(nameof(Input) + "." + problem.Field, problem.Message);
            }

            return;
        }

        ApiProblem refusal = api.Problem!;
        if (refusal.Code == RegistrationTaken)
        {
            ModelState.AddModelError(nameof(Input) + "." + nameof(BusFormInput.RegistrationNumber), BusFormValidator.RegistrationTakenMessage);
            return;
        }

        Alert = AlertFor(refusal);
        // A bus that is gone or another company's shows nothing of it; every other refusal keeps
        // what the operator typed.
        ShowsForm = refusal.Code != FleetDisplay.NotFound && refusal.Code != SharedProblems.Forbidden;
    }

    private PageAlert AlertFor(ApiProblem problem)
    {
        if (problem.Code == BusInUse)
        {
            string tripCount = problem.GetExtension("futureTripCount") ?? string.Empty;
            string? nextDepartureAt = problem.GetExtension("nextDepartureAt");
            string nextDeparture = IndiaTime.FormatIso(nextDepartureAt) ?? string.Empty;
            return PageAlert.Warning(Messages.ProblemBusInUse(tripCount, nextDeparture) + " " + SeatsLockedText);
        }

        PageAlert alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
        if (problem.Code == FleetDisplay.NotFound)
        {
            return alert.WithLink(BackToListText, BusListAddress);
        }

        return alert;
    }

    private static BusFormInput ToForm(SavedBus bus)
    {
        var input = new BusFormInput
        {
            Name = bus.Name,
            RegistrationNumber = bus.RegistrationNumber,
            BusType = bus.BusType,
            IsAc = bus.IsAc,
            Amenities = new List<string>(bus.Amenities),
            SeatClasses = new List<string?>(bus.SeatClasses),
        };
        foreach (SavedDeck deck in bus.Layout.Decks)
        {
            BusFormDeck size = input.Deck(deck.Deck);
            size.Rows = deck.Rows;
            size.Columns = deck.Columns;
            size.AisleAfterColumn = deck.AisleAfterColumn;
            if (deck.Deck == BusFormInput.UpperDeck)
            {
                input.HasUpperDeck = true;
            }
        }

        foreach (SavedSeat seat in bus.Layout.Seats)
        {
            input.Seats.Add(ToFormSeat(seat));
        }

        input.RememberSeatClasses();
        return input;
    }

    private static BusFormSeat ToFormSeat(SavedSeat seat)
    {
        return new BusFormSeat
        {
            Label = seat.Label,
            Deck = seat.Deck,
            Row = seat.Row,
            Column = seat.Column,
            SeatClass = seat.SeatClass,
        };
    }
}
