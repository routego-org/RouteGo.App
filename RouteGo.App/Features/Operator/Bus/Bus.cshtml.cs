namespace RouteGo.App.Features.Operator.Bus;

/// <summary>One deck of the seat layout: its size and where the aisle runs.</summary>
public sealed record BusDeck(string Deck, int Rows, int Columns)
{
    /// <summary>The column the aisle runs after; null when the deck has no aisle.</summary>
    public int? AisleAfterColumn { get; init; }
}

/// <summary>One seat: its seat number, place on a deck, and seat class.</summary>
public sealed record BusSeat
{
    public required string Label { get; init; }

    public required string Deck { get; init; }

    public required int Row { get; init; }

    public required int Column { get; init; }

    public required string SeatClass { get; init; }
}

/// <summary>The decks (lower first) and every seat on them.</summary>
public sealed record BusLayout(IReadOnlyList<BusDeck> Decks, IReadOnlyList<BusSeat> Seats);

/// <summary>One uploaded photo of the bus: only its address is shown here.</summary>
public sealed record BusPhoto(Guid PhotoId, string? Url);

/// <summary>One of the operator's buses as Fleet returns it.</summary>
public sealed record BusDetails
{
    public required Guid BusId { get; init; }

    /// <summary>Upper case, without spaces or hyphens.</summary>
    public required string RegistrationNumber { get; init; }

    public required string Name { get; init; }

    /// <summary>seater, sleeper or semiSleeper.</summary>
    public required string BusType { get; init; }

    public required bool IsAc { get; init; }

    public required IReadOnlyList<string> Amenities { get; init; }

    public required IReadOnlyList<string> SeatClasses { get; init; }

    public required BusLayout Layout { get; init; }

    public required int TotalSeats { get; init; }

    /// <summary>active or inactive.</summary>
    public required string Status { get; init; }

    /// <summary>The uploaded photos in order; the first is the cover photo.</summary>
    public required IReadOnlyList<BusPhoto> Photos { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsActive
    {
        get { return Status == "active"; }
    }
}

public sealed class BusHandler
{
    private const string BusesPath = "buses";

    private readonly IHttpClientFactory _httpClientFactory;

    public BusHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The operator's bus with its seat classes and seat layout.</summary>
    public async Task<ApiResult<BusDetails>> GetBusAsync(Guid busId, CancellationToken cancellationToken)
    {
        return await SendAsync(HttpMethod.Get, $"{BusesPath}/{busId}", cancellationToken);
    }

    /// <summary>
    /// Takes the bus out of service, so nobody can put it on a schedule. Fleet refuses while the bus
    /// still has upcoming trips.
    /// </summary>
    public async Task<ApiResult<BusDetails>> DeactivateBusAsync(Guid busId, CancellationToken cancellationToken)
    {
        return await SendAsync(HttpMethod.Post, $"{BusesPath}/{busId}/deactivate", cancellationToken);
    }

    /// <summary>Puts the bus back in service.</summary>
    public async Task<ApiResult<BusDetails>> ActivateBusAsync(Guid busId, CancellationToken cancellationToken)
    {
        return await SendAsync(HttpMethod.Post, $"{BusesPath}/{busId}/activate", cancellationToken);
    }

    private async Task<ApiResult<BusDetails>> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(method, path);
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<BusDetails>(cancellationToken);
    }
}

public sealed class BusModel : PageModel
{
    public const string BusListAddress = "/operator/buses";
    public const string BackToListText = "Back to my buses";

    private const string BusPage = "/Operator/Bus/Bus";
    private const string Entity = "bus";
    private const string EntityLabel = "Bus";
    private const string BusInUse = "BUS_IN_USE";

    private readonly BusHandler _handler;

    public BusModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new BusHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public Guid BusId { get; set; }

    /// <summary>Set by the "Deactivate bus" link: the question is asked before anything is sent.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Deactivate { get; set; }

    /// <summary>The loaded bus; null when it could not be loaded or may not be shown.</summary>
    public BusDetails? Bus { get; private set; }

    /// <summary>The seat layout drawn deck by deck; empty when no bus is shown.</summary>
    public IReadOnlyList<SeatGridDeck> SeatGrid { get; private set; } = Array.Empty<SeatGridDeck>();

    /// <summary>Whether the deactivate question is shown.</summary>
    public bool IsConfirming { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<BusDetails> api = await _handler.GetBusAsync(BusId, cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = AlertFor(api.Problem!);
            return;
        }

        Show(api.Value!);
        IsConfirming = Deactivate && Bus!.IsActive;
    }

    public async Task<IActionResult> OnPostDeactivateAsync(CancellationToken cancellationToken)
    {
        ApiResult<BusDetails> api = await _handler.DeactivateBusAsync(BusId, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = Messages.SuccessDeactivated(EntityLabel);
            return RedirectToPage(BusPage, new { busId = BusId });
        }

        // Upcoming trips cannot be cleared from here, so asking again would only be refused again;
        // when Fleet was down or failed, the question stays open so the operator can simply retry.
        bool asksAgain = api.Problem!.Code != BusInUse;
        return await ShowRefusalAsync(api.Problem!, asksAgain, cancellationToken);
    }

    public async Task<IActionResult> OnPostActivateAsync(CancellationToken cancellationToken)
    {
        ApiResult<BusDetails> api = await _handler.ActivateBusAsync(BusId, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = Messages.SuccessActivated(EntityLabel);
            return RedirectToPage(BusPage, new { busId = BusId });
        }

        return await ShowRefusalAsync(api.Problem!, false, cancellationToken);
    }

    /// <summary>The bus page's own address: the Cancel link.</summary>
    public string BusAddress()
    {
        return Url.Page(BusPage, new { busId = BusId })!;
    }

    /// <summary>The bus page address that opens the deactivate question.</summary>
    public string ConfirmAddress()
    {
        return Url.Page(BusPage, new { busId = BusId, deactivate = true })!;
    }

    /// <summary>Where the deactivate question posts.</summary>
    public string DeactivateAddress()
    {
        return Url.Page(BusPage, "Deactivate", new { busId = BusId })!;
    }

    /// <summary>Where the "Activate bus" button posts.</summary>
    public string ActivateAddress()
    {
        return Url.Page(BusPage, "Activate", new { busId = BusId })!;
    }

    public string EditAddress()
    {
        return $"/operator/buses/{BusId}/edit";
    }

    /// <summary>The page where the operator adds, orders and deletes the bus's photos.</summary>
    public string PhotosAddress()
    {
        return $"/operator/buses/{BusId}/photos";
    }

    /// <summary>The public bus page, as customers see it.</summary>
    public string PublicAddress()
    {
        return $"/buses/{BusId}";
    }

    private async Task<IActionResult> ShowRefusalAsync(ApiProblem problem, bool asksAgain, CancellationToken cancellationToken)
    {
        Alert = AlertFor(problem);
        if (FleetDisplay.HidesRecord(problem))
        {
            return Page();
        }

        // Nothing changed, so the bus is read again and shown as it still is.
        ApiResult<BusDetails> reload = await _handler.GetBusAsync(BusId, cancellationToken);
        if (reload.IsSuccess)
        {
            Show(reload.Value!);
            IsConfirming = asksAgain && Bus!.IsActive;
        }

        return Page();
    }

    private void Show(BusDetails bus)
    {
        Bus = bus;
        SeatGrid = SeatGridDeck.Build(bus.Layout);
    }

    private PageAlert AlertFor(ApiProblem problem)
    {
        if (problem.Code == BusInUse)
        {
            string tripCount = problem.GetExtension("futureTripCount") ?? string.Empty;
            string? nextDepartureAt = problem.GetExtension("nextDepartureAt");
            string nextDeparture = IndiaTime.FormatIso(nextDepartureAt) ?? string.Empty;
            var inUse = PageAlert.Warning(Messages.ProblemBusInUse(tripCount, nextDeparture));
            return inUse.WithLink("See this bus's schedules", $"/operator/schedules?busId={BusId}");
        }

        PageAlert alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
        if (problem.Code == FleetDisplay.NotFound)
        {
            return alert.WithLink(BackToListText, BusListAddress);
        }

        return alert;
    }
}
