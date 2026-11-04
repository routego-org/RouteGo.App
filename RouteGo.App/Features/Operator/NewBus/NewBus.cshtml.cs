namespace RouteGo.App.Features.Operator.NewBus;

/// <summary>The field of Fleet's new bus this page uses.</summary>
public sealed record CreatedBus(Guid BusId);

public sealed class NewBusHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NewBusHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Asks Fleet to add the bus, active, with its seat classes and seat layout.</summary>
    public async Task<ApiResult<CreatedBus>> CreateBusAsync(BusRequest bus, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Post, "buses")
        {
            Content = JsonContent.Create(bus, options: WireJson.Options),
        };
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<CreatedBus>(cancellationToken);
    }
}

public sealed class NewBusModel : PageModel
{
    public const string CancelAddress = "/operator/buses";

    private const string NewBusPage = "/Operator/NewBus/NewBus";
    private const string BusPage = "/Operator/Bus/Bus";
    private const string Entity = "bus";
    private const string EntityLabel = "Bus";
    private const string RegistrationTaken = "BUS_REGISTRATION_ALREADY_EXISTS";

    private readonly NewBusHandler _handler;

    public NewBusModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new NewBusHandler(httpClientFactory);
    }

    [BindProperty]
    public BusFormInput Input { get; set; } = new BusFormInput();

    /// <summary>The builder step a builder button posts ("fill:lower"); empty for "Save bus".</summary>
    [BindProperty]
    public string? BuilderAction { get; set; }

    public BusFormOutcome Outcome { get; private set; } = new BusFormOutcome();

    public PageAlert? Alert { get; private set; }

    public void OnGet()
    {
        Input.EnsureOneSeatClass();
        Input.RememberSeatClasses();
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

        ApiResult<CreatedBus> api = await _handler.CreateBusAsync(BusRequest.From(Input), cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = Messages.SuccessCreated(EntityLabel);
            return RedirectToPage(BusPage, new { busId = api.Value!.BusId });
        }

        ShowRefusal(api);
        return Page();
    }

    /// <summary>What the shared bus form partial draws.</summary>
    public BusFormView View()
    {
        return new BusFormView
        {
            Input = Input,
            Outcome = Outcome,
            SaveAddress = Url.Page(NewBusPage)!,
            BuildAddress = Url.Page(NewBusPage, "Build")!,
            CancelAddress = CancelAddress,
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

    private void ShowRefusal(ApiResult<CreatedBus> api)
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

        Alert = FleetDisplay.ToAlert(refusal, Entity, CorrelationId.Get(HttpContext));
    }
}
