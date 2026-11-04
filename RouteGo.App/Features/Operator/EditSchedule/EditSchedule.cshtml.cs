namespace RouteGo.App.Features.Operator.EditSchedule;

public sealed class EditScheduleModel : PageModel
{
    public const string ScheduleListAddress = "/operator/schedules";
    public const string BackToListText = "Back to my schedules";
    public const string BackToScheduleText = "Back to the schedule";

    private const string EditSchedulePage = "/Operator/EditSchedule/EditSchedule";
    private const string SchedulePage = "/Operator/Schedule/Schedule";
    private const string Entity = "schedule";

    private readonly EditScheduleHandler _handler;

    public EditScheduleModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new EditScheduleHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public Guid ScheduleId { get; set; }

    [BindProperty]
    public ScheduleFormInput Input { get; set; } = new ScheduleFormInput();

    /// <summary>Sent by "Save and change trips": the operator has seen that upcoming trips will be cancelled.</summary>
    [BindProperty]
    public bool Confirmed { get; set; }

    /// <summary>The schedule as saved; null when it could not be loaded or may not be shown.</summary>
    public SavedSchedule? Schedule { get; private set; }

    /// <summary>The routes and buses to choose from; null when there is no form.</summary>
    public EditScheduleChoices? Choices { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the page asks before saving a change that cancels and remakes trips.</summary>
    public bool IsAsking { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        bool isShown = await ShowScheduleAsync(cancellationToken);
        if (!isShown)
        {
            return;
        }

        SavedSchedule schedule = Schedule!;
        Input = new ScheduleFormInput
        {
            RouteId = schedule.RouteId,
            BusId = schedule.BusId,
            DepartureTime = schedule.DepartureTime,
            Days = new List<string>(schedule.DaysOfWeek),
            Fares = ScheduleFormInput.FaresFrom(schedule.ClassFares),
        };
        await UseBusSeatClassesAsync(cancellationToken);
    }

    // Choosing a bus only changes the form: the page is drawn again with its fare boxes, and
    // nothing is saved. The boxes move, so the posted values are drawn from Input, not ModelState.
    public async Task<IActionResult> OnPostShowFaresAsync(CancellationToken cancellationToken)
    {
        return await RedrawAsync(cancellationToken);
    }

    /// <summary>"Go back" from the question: the form again with everything kept.</summary>
    public async Task<IActionResult> OnPostBackAsync(CancellationToken cancellationToken)
    {
        return await RedrawAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        bool isReady = await PrepareSaveAsync(cancellationToken);
        if (!isReady)
        {
            return Page();
        }

        if (!Confirmed && ChangesTrips())
        {
            IsAsking = true;
            return Page();
        }

        ApiResult<ScheduleChange> api = await _handler.ReplaceScheduleAsync(ScheduleId, ScheduleRequest.From(Input), cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = api.Value!.SavedText();
            return RedirectToPage(SchedulePage, new { scheduleId = ScheduleId });
        }

        await ShowRefusalAsync(api, cancellationToken);
        return Page();
    }

    /// <summary>"Change {schedule name}", named from the schedule as saved; null while no schedule is shown.</summary>
    public string? Title()
    {
        if (Schedule is null || Choices is null)
        {
            return null;
        }

        ScheduleFormChoice? route = Choices.Routes.FirstOrDefault(choice => choice.Id == Schedule.RouteId);
        string routeName = TripDisplay.UnknownRoute;
        if (route != null)
        {
            routeName = route.Name;
        }

        return "Change " + TripDisplay.ScheduleName(routeName, Schedule.DepartureTime);
    }

    /// <summary>What the shared schedule form partial draws.</summary>
    public ScheduleFormView View()
    {
        string? goBackAddress = null;
        if (IsAsking)
        {
            goBackAddress = Url.Page(EditSchedulePage, "Back", new { scheduleId = ScheduleId });
        }

        return new ScheduleFormView
        {
            Input = Input,
            Routes = Choices!.Routes,
            Buses = Choices.Buses,
            SaveAddress = Url.Page(EditSchedulePage, new { scheduleId = ScheduleId })!,
            ShowFaresAddress = Url.Page(EditSchedulePage, "ShowFares", new { scheduleId = ScheduleId })!,
            CancelAddress = ScheduleAddress(),
            IsChange = true,
            GoBackAddress = goBackAddress,
        };
    }

    private string ScheduleAddress()
    {
        return Url.Page(SchedulePage, new { scheduleId = ScheduleId })!;
    }

    private async Task<IActionResult> RedrawAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        bool isShown = await ShowScheduleAsync(cancellationToken);
        if (isShown)
        {
            await UseBusSeatClassesAsync(cancellationToken);
        }

        return Page();
    }

    // The schedule and the choices, or the message instead of the form: a stopped schedule cannot be changed.
    private async Task<bool> ShowScheduleAsync(CancellationToken cancellationToken)
    {
        ApiResult<SavedSchedule> schedule = await _handler.GetScheduleAsync(ScheduleId, cancellationToken);
        if (!schedule.IsSuccess)
        {
            HideForm(schedule.Problem!);
            return false;
        }

        if (!schedule.Value!.IsActive)
        {
            Alert = InactiveAlert();
            return false;
        }

        ApiResult<EditScheduleChoices> choices = await _handler.ListChoicesAsync(schedule.Value, cancellationToken);
        if (!choices.IsSuccess)
        {
            ShowChoicesRefusal(choices);
            return false;
        }

        Schedule = schedule.Value;
        Choices = choices.Value;
        return true;
    }

    // The schedule is read again, the boxes follow the chosen bus, then every field is checked;
    // false when nothing may be sent.
    private async Task<bool> PrepareSaveAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        bool isShown = await ShowScheduleAsync(cancellationToken);
        if (!isShown)
        {
            return false;
        }

        bool hasSeatClasses = await UseBusSeatClassesAsync(cancellationToken);
        var inactiveBusIds = Choices!.Buses.Where(bus => !bus.IsActive).Select(bus => bus.Id).ToList();
        var validator = new ScheduleFormValidator(inactiveBusIds);
        ValidationResult validation = await validator.ValidateAsync(Input, cancellationToken);
        validation.AddToModelState(ModelState, nameof(Input));
        return validation.IsValid && hasSeatClasses;
    }

    // Only a new route, bus, time or days cancels and remakes trips; new fares only re-price them.
    private bool ChangesTrips()
    {
        SavedSchedule schedule = Schedule!;
        var savedDays = new ScheduleFormInput { Days = new List<string>(schedule.DaysOfWeek) };
        bool sameDays = Input.DaysInWeekOrder().SequenceEqual(savedDays.DaysInWeekOrder());
        bool sameTime = Input.DepartureTimeForFleet() == schedule.DepartureTime;
        return Input.RouteId != schedule.RouteId || Input.BusId != schedule.BusId || !sameTime || !sameDays;
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

    private async Task ShowRefusalAsync(ApiResult<ScheduleChange> api, CancellationToken cancellationToken)
    {
        if (api.Errors != null)
        {
            Alert = ScheduleFormRefusals.PlaceFieldErrors(api.Errors, api.ErrorsTitle, ModelState);
            return;
        }

        ApiProblem problem = api.Problem!;
        if (problem.Code == TripDisplay.ScheduleInactive || problem.Code == FleetDisplay.NotFound || problem.Code == SharedProblems.Forbidden)
        {
            HideForm(problem);
            return;
        }

        Alert = ScheduleFormRefusals.Show(problem, Input, ModelState, CorrelationId.Get(HttpContext));
        if (problem.Code == TripDisplay.ClassFaresMismatch)
        {
            await UseBusSeatClassesAsync(cancellationToken);
        }
    }

    // A schedule that is stopped, gone or another company's shows only the message.
    private void HideForm(ApiProblem problem)
    {
        Schedule = null;
        Choices = null;
        if (problem.Code == TripDisplay.ScheduleInactive)
        {
            Alert = InactiveAlert();
            return;
        }

        PageAlert alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
        if (problem.Code == FleetDisplay.NotFound)
        {
            alert = alert.WithLink(BackToListText, ScheduleListAddress);
        }

        Alert = alert;
    }

    // The page sends only fixed list settings, so a refused one is a fault on our side.
    private void ShowChoicesRefusal(ApiResult<EditScheduleChoices> choices)
    {
        string correlationId = CorrelationId.Get(HttpContext);
        if (choices.Errors != null)
        {
            Alert = PageAlert.Danger(Messages.ProblemInternalError(correlationId));
            return;
        }

        Alert = FleetDisplay.ToAlert(choices.Problem!, Entity, correlationId);
    }

    private PageAlert InactiveAlert()
    {
        return PageAlert.Danger(Messages.ProblemScheduleInactive).WithLink(BackToScheduleText, ScheduleAddress());
    }
}
