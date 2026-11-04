namespace RouteGo.App.Features.Operator.NewSchedule;

/// <summary>The operator's active routes and buses: a new schedule may use only these.</summary>
public sealed record NewScheduleChoices(IReadOnlyList<ScheduleFormChoice> Routes, IReadOnlyList<ScheduleFormChoice> Buses);

/// <summary>The fields of Fleet's new schedule this page uses.</summary>
public sealed record CreatedSchedule(Guid ScheduleId, DateOnly TripsGeneratedThrough);

public sealed class NewScheduleHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ScheduleFormFleet _fleet;

    public NewScheduleHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _fleet = new ScheduleFormFleet(httpClientFactory);
    }

    /// <summary>The active routes and buses to choose from; the first refusal of either list is the answer.</summary>
    public async Task<ApiResult<NewScheduleChoices>> ListChoicesAsync(CancellationToken cancellationToken)
    {
        Task<ApiResult<IReadOnlyList<ScheduleFormChoice>>> routes = _fleet.ListRoutesAsync(ScheduleFormFleet.ActiveStatus, cancellationToken);
        Task<ApiResult<IReadOnlyList<ScheduleFormChoice>>> buses = _fleet.ListBusesAsync(ScheduleFormFleet.ActiveStatus, cancellationToken);
        await Task.WhenAll(routes, buses);
        ApiResult<IReadOnlyList<ScheduleFormChoice>> routeAnswer = await routes;
        ApiResult<IReadOnlyList<ScheduleFormChoice>> busAnswer = await buses;
        if (!routeAnswer.IsSuccess)
        {
            return new ApiResult<NewScheduleChoices>(null, routeAnswer.Errors, routeAnswer.Problem);
        }

        if (!busAnswer.IsSuccess)
        {
            return new ApiResult<NewScheduleChoices>(null, busAnswer.Errors, busAnswer.Problem);
        }

        return ApiResult<NewScheduleChoices>.Success(new NewScheduleChoices(routeAnswer.Value!, busAnswer.Value!));
    }

    /// <summary>The chosen bus's seat classes, one fare box each.</summary>
    public async Task<ApiResult<ScheduleFormSeatClasses>> GetSeatClassesAsync(Guid busId, CancellationToken cancellationToken)
    {
        return await _fleet.GetSeatClassesAsync(busId, cancellationToken);
    }

    /// <summary>Creates the schedule; Fleet at once puts its next 14 days of trips on sale.</summary>
    public async Task<ApiResult<CreatedSchedule>> CreateScheduleAsync(ScheduleRequest schedule, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Post, "schedules")
        {
            Content = JsonContent.Create(schedule, options: WireJson.Options),
        };
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<CreatedSchedule>(cancellationToken);
    }
}

public sealed class NewScheduleModel : PageModel
{
    public const string CancelAddress = "/operator/schedules";
    public const string NewRouteAddress = "/operator/routes/new";
    public const string NewBusAddress = "/operator/buses/new";

    private const string NewSchedulePage = "/Operator/NewSchedule/NewSchedule";
    private const string SchedulePage = "/Operator/Schedule/Schedule";
    private const string Entity = "schedule";

    private readonly NewScheduleHandler _handler;

    public NewScheduleModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new NewScheduleHandler(httpClientFactory);
    }

    [BindProperty]
    public ScheduleFormInput Input { get; set; } = new ScheduleFormInput();

    /// <summary>The routes and buses to choose from; null when they could not be loaded, and then there is no form.</summary>
    public NewScheduleChoices? Choices { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the operator lacks an active route or an active bus, so no schedule can be made yet.</summary>
    public bool HasNothingToSchedule
    {
        get { return Choices != null && (Choices.Routes.Count == 0 || Choices.Buses.Count == 0); }
    }

    /// <summary>Opens the form; <paramref name="routeId"/> comes from a route page's "Add a schedule on this route".</summary>
    public async Task OnGetAsync(Guid? routeId, CancellationToken cancellationToken)
    {
        bool hasChoices = await ListChoicesAsync(cancellationToken);
        if (!hasChoices || routeId is null)
        {
            return;
        }

        bool isOffered = Choices!.Routes.Any(route => route.Id == routeId.Value);
        if (isOffered)
        {
            Input.RouteId = routeId;
        }
    }

    // Choosing a bus only changes the form: the page is drawn again with its fare boxes, and
    // nothing is saved. The boxes move, so the posted values are drawn from Input, not ModelState.
    public async Task<IActionResult> OnPostShowFaresAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        bool hasChoices = await ListChoicesAsync(cancellationToken);
        if (hasChoices)
        {
            await UseBusSeatClassesAsync(cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        bool hasChoices = await ListChoicesAsync(cancellationToken);
        if (!hasChoices)
        {
            return Page();
        }

        // The fare boxes follow the chosen bus even when "Show fares for this bus" was not pressed.
        bool hasSeatClasses = await UseBusSeatClassesAsync(cancellationToken);
        var validator = new ScheduleFormValidator(Array.Empty<Guid>());
        ValidationResult validation = await validator.ValidateAsync(Input, cancellationToken);
        validation.AddToModelState(ModelState, nameof(Input));
        if (!validation.IsValid || !hasSeatClasses)
        {
            return Page();
        }

        ApiResult<CreatedSchedule> api = await _handler.CreateScheduleAsync(ScheduleRequest.From(Input), cancellationToken);
        if (api.IsSuccess)
        {
            string until = IndiaTime.FormatDay(api.Value!.TripsGeneratedThrough);
            TempData[Flash.Key] = $"Schedule created. Trips are on sale until {until}.";
            return RedirectToPage(SchedulePage, new { scheduleId = api.Value.ScheduleId });
        }

        await ShowRefusalAsync(api, cancellationToken);
        return Page();
    }

    /// <summary>What the shared schedule form partial draws.</summary>
    public ScheduleFormView View()
    {
        return new ScheduleFormView
        {
            Input = Input,
            Routes = Choices!.Routes,
            Buses = Choices.Buses,
            SaveAddress = Url.Page(NewSchedulePage)!,
            ShowFaresAddress = Url.Page(NewSchedulePage, "ShowFares")!,
            CancelAddress = CancelAddress,
        };
    }

    private async Task<bool> ListChoicesAsync(CancellationToken cancellationToken)
    {
        ApiResult<NewScheduleChoices> api = await _handler.ListChoicesAsync(cancellationToken);
        if (api.IsSuccess)
        {
            Choices = api.Value;
            return true;
        }

        // The page sends only fixed list settings, so a refused one is a fault on our side.
        string correlationId = CorrelationId.Get(HttpContext);
        if (api.Errors != null)
        {
            Alert = PageAlert.Danger(Messages.ProblemInternalError(correlationId));
            return false;
        }

        Alert = FleetDisplay.ToAlert(api.Problem!, Entity, correlationId);
        return false;
    }

    // False when the chosen bus's seat classes could not be read: the boxes stay as they were and nothing may be saved.
    private async Task<bool> UseBusSeatClassesAsync(CancellationToken cancellationToken)
    {
        if (Input.BusId is null)
        {
            Input.Fares.Clear();
            return true;
        }

        ApiResult<ScheduleFormSeatClasses> api = await _handler.GetSeatClassesAsync(Input.BusId.Value, cancellationToken);
        if (!api.IsSuccess)
        {
            ModelState.AddModelError(nameof(Input) + "." + nameof(ScheduleFormInput.BusId), ScheduleFormRefusals.SeatClassesFailedMessage);
            return false;
        }

        Input.UseSeatClasses(api.Value!.SeatClasses);
        return true;
    }

    private async Task ShowRefusalAsync(ApiResult<CreatedSchedule> api, CancellationToken cancellationToken)
    {
        if (api.Errors != null)
        {
            Alert = ScheduleFormRefusals.PlaceFieldErrors(api.Errors, api.ErrorsTitle, ModelState);
            return;
        }

        Alert = ScheduleFormRefusals.Show(api.Problem!, Input, ModelState, CorrelationId.Get(HttpContext));
        if (api.Problem!.Code == TripDisplay.ClassFaresMismatch)
        {
            await UseBusSeatClassesAsync(cancellationToken);
        }
    }
}
