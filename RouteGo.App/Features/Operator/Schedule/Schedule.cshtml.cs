namespace RouteGo.App.Features.Operator.Schedule;

public sealed class ScheduleModel : PageModel
{
    public const string ScheduleListAddress = "/operator/schedules";
    public const string BackToListText = "Back to my schedules";

    /// <summary>How many days ahead trips may be put on sale (Fleet's trip horizon).</summary>
    public const int SaleHorizonDays = 60;

    private const string SchedulePage = "/Operator/Schedule/Schedule";
    private const string Entity = "schedule";
    private const string EndDateOutOfRange = "TRIP_END_DATE_OUT_OF_RANGE";
    private const string EndDateField = "endDate";

    private readonly ScheduleHandler _handler;
    private readonly IClock _clock;

    public ScheduleModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new ScheduleHandler(httpClientFactory);
        _clock = clock;
    }

    [BindProperty(SupportsGet = true)]
    public Guid ScheduleId { get; set; }

    /// <summary>Set by the "Stop schedule" link: the question is asked before anything is sent.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Stop { get; set; }

    [BindProperty]
    public ScheduleInput Input { get; set; } = new ScheduleInput();

    /// <summary>The loaded schedule with its route and bus; null when it could not be loaded or may not be shown.</summary>
    public SchedulePageData? Data { get; private set; }

    /// <summary>Whether the stop question is shown.</summary>
    public bool IsConfirming { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Today in India: the first date the operator may choose.</summary>
    public DateOnly Today()
    {
        return IndiaTime.Today(_clock.UtcNow);
    }

    /// <summary>The last date the operator may choose.</summary>
    public DateOnly LatestDate()
    {
        return Today().AddDays(SaleHorizonDays);
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        bool isShown = await ShowScheduleAsync(cancellationToken);
        if (isShown)
        {
            IsConfirming = Stop && Data!.Schedule.IsActive;
        }
    }

    public async Task<IActionResult> OnPostExtendAsync(CancellationToken cancellationToken)
    {
        var validator = new ScheduleValidator(Today(), LatestDate());
        ValidationResult validation = await validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            await ShowScheduleAsync(cancellationToken);
            return Page();
        }

        ApiResult<TripExtension> api = await _handler.CreateTripsAsync(ScheduleId, Input.Date()!.Value, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = ExtendedText(api.Value!);
            return RedirectToPage(SchedulePage, new { scheduleId = ScheduleId });
        }

        await ShowExtendRefusalAsync(api, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostStopAsync(CancellationToken cancellationToken)
    {
        ApiResult<StoppedSchedule> api = await _handler.DeactivateScheduleAsync(ScheduleId, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = $"Schedule stopped. {api.Value!.CancelledTripCount} upcoming trips were cancelled.";
            return RedirectToPage(SchedulePage, new { scheduleId = ScheduleId });
        }

        // Nothing changed, so the question stays open and the operator can simply try again.
        await ShowRefusalAsync(api.Problem!, true, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRestartAsync(CancellationToken cancellationToken)
    {
        ApiResult<ScheduleDetails> api = await _handler.ActivateScheduleAsync(ScheduleId, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = "Schedule restarted. No trips are on sale yet. Choose a date and put trips on sale.";
            return RedirectToPage(SchedulePage, new { scheduleId = ScheduleId });
        }

        await ShowRefusalAsync(api.Problem!, false, cancellationToken);
        return Page();
    }

    /// <summary>The schedule page's own address: "Go back" from the stop question.</summary>
    public string ScheduleAddress()
    {
        return Url.Page(SchedulePage, new { scheduleId = ScheduleId })!;
    }

    /// <summary>The schedule page address that opens the stop question.</summary>
    public string ConfirmAddress()
    {
        return Url.Page(SchedulePage, new { scheduleId = ScheduleId, stop = "true" })!;
    }

    public string ExtendAddress()
    {
        return Url.Page(SchedulePage, "Extend", new { scheduleId = ScheduleId })!;
    }

    public string StopAddress()
    {
        return Url.Page(SchedulePage, "Stop", new { scheduleId = ScheduleId })!;
    }

    public string RestartAddress()
    {
        return Url.Page(SchedulePage, "Restart", new { scheduleId = ScheduleId })!;
    }

    public string EditAddress()
    {
        return $"/operator/schedules/{ScheduleId}/edit";
    }

    /// <summary>The trip list showing only this schedule's trips.</summary>
    public string TripsAddress()
    {
        return "/operator/trips?scheduleId=" + ScheduleId;
    }

    private async Task<bool> ShowScheduleAsync(CancellationToken cancellationToken)
    {
        ApiResult<SchedulePageData> api = await _handler.GetPageDataAsync(ScheduleId, cancellationToken);
        if (!api.IsSuccess)
        {
            Data = null;
            Alert = AlertFor(api.Problem!);
            return false;
        }

        Data = api.Value;
        return true;
    }

    private async Task ShowRefusalAsync(ApiProblem problem, bool asksAgain, CancellationToken cancellationToken)
    {
        if (FleetDisplay.HidesRecord(problem))
        {
            Alert = AlertFor(problem);
            return;
        }

        // Nothing changed, so the schedule is read again and shown as it now is.
        bool isShown = await ShowScheduleAsync(cancellationToken);
        if (!isShown)
        {
            return;
        }

        Alert = AlertFor(problem);
        IsConfirming = asksAgain && Data!.Schedule.IsActive;
    }

    // A refused date is told at the date box; every other refusal at the top.
    private async Task ShowExtendRefusalAsync(ApiResult<TripExtension> api, CancellationToken cancellationToken)
    {
        if (api.Errors != null)
        {
            ShowDateErrors(api.Errors, api.ErrorsTitle);
            await ShowScheduleAsync(cancellationToken);
            return;
        }

        if (api.Problem!.Code == EndDateOutOfRange)
        {
            ModelState.AddModelError(nameof(Input) + "." + nameof(ScheduleInput.EndDate), OutOfRangeText(api.Problem));
            await ShowScheduleAsync(cancellationToken);
            return;
        }

        await ShowRefusalAsync(api.Problem, false, cancellationToken);
    }

    // Fleet names the date "endDate"; any other field it refuses is not on this page, so its title is told at the top.
    private void ShowDateErrors(IReadOnlyDictionary<string, string[]> errors, string? title)
    {
        foreach (KeyValuePair<string, string[]> error in errors)
        {
            if (string.Equals(error.Key, EndDateField, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(Input) + "." + nameof(ScheduleInput.EndDate), Messages.ValidationRequired(ScheduleValidator.DateLabel));
                continue;
            }

            Alert = PageAlert.Danger(title ?? error.Value[0]);
        }
    }

    private string OutOfRangeText(ApiProblem problem)
    {
        DateOnly? fleetLatest = ScheduleInput.ParseDate(problem.GetExtension("latestAllowedDate"));
        if (fleetLatest is null)
        {
            return ScheduleValidator.OutOfRangeMessage(LatestDate());
        }

        return ScheduleValidator.OutOfRangeMessage(fleetLatest.Value);
    }

    private static string ExtendedText(TripExtension extension)
    {
        string until = IndiaTime.FormatDay(extension.TripsGeneratedThrough);
        if (extension.Items.Count == 0)
        {
            return $"No new trips were needed. Trips are on sale until {until}.";
        }

        return $"{extension.Items.Count} more trips are now on sale. Trips are on sale until {until}.";
    }

    private PageAlert AlertFor(ApiProblem problem)
    {
        if (problem.Code == TripDisplay.BusTripOverlap)
        {
            return TripDisplay.OverlapAlert(problem);
        }

        if (problem.Code == TripDisplay.BusInactive && Data != null)
        {
            return TripDisplay.BusInactiveAlert(Data.Schedule.BusId);
        }

        if (problem.Code == TripDisplay.ScheduleInactive)
        {
            return PageAlert.Danger(Messages.ProblemScheduleInactive);
        }

        PageAlert alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
        if (problem.Code == FleetDisplay.NotFound)
        {
            return alert.WithLink(BackToListText, ScheduleListAddress);
        }

        return alert;
    }
}
