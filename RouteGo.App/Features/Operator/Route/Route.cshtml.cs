namespace RouteGo.App.Features.Operator.Route;

/// <summary>A city from Fleet's city list.</summary>
public sealed record City(Guid CityId, string Name, string State);

/// <summary>One stop of a route: its place in the route, its city, and minutes after leaving the first stop.</summary>
public sealed record RouteStop(int Sequence, City City, int OffsetMinutes);

/// <summary>One of the operator's routes as Fleet returns it.</summary>
public sealed record RouteDetails
{
    public required Guid RouteId { get; init; }

    /// <summary>The stops in order, the first city first.</summary>
    public required IReadOnlyList<RouteStop> Stops { get; init; }

    public required int DistanceKm { get; init; }

    /// <summary>The last stop's time from start.</summary>
    public required int JourneyMinutes { get; init; }

    /// <summary>active or inactive.</summary>
    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset? DeactivatedAt { get; init; }

    public bool IsActive
    {
        get { return Status == "active"; }
    }

    /// <summary>The route's name, made from its cities in stop order.</summary>
    public string Name()
    {
        var cityNames = new List<string>();
        foreach (RouteStop stop in Stops)
        {
            cityNames.Add(stop.City.Name);
        }

        return FleetDisplay.RouteName(cityNames);
    }
}

public sealed class RouteHandler
{
    private const string RoutesPath = "routes";

    private readonly IHttpClientFactory _httpClientFactory;

    public RouteHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The operator's route with every stop in order.</summary>
    public async Task<ApiResult<RouteDetails>> GetRouteAsync(Guid routeId, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{RoutesPath}/{routeId}");
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<RouteDetails>(cancellationToken);
    }

    /// <summary>
    /// Stops new schedules using the route. Schedules and trips already on it keep running; Fleet
    /// offers no way to turn it back on.
    /// </summary>
    public async Task<ApiResult<RouteDetails>> DeactivateRouteAsync(Guid routeId, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{RoutesPath}/{routeId}/deactivate");
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<RouteDetails>(cancellationToken);
    }
}

public sealed class RouteModel : PageModel
{
    public const string RouteListAddress = "/operator/routes";
    public const string BackToListText = "Back to my routes";

    private const string RoutePage = "/Operator/Route/Route";
    private const string Entity = "route";
    private const string EntityLabel = "Route";

    private readonly RouteHandler _handler;

    public RouteModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new RouteHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public Guid RouteId { get; set; }

    /// <summary>Set by the "Deactivate route" link: the question is asked before anything is sent.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Deactivate { get; set; }

    /// <summary>The loaded route; null when it could not be loaded or may not be shown.</summary>
    public RouteDetails? Route { get; private set; }

    /// <summary>Whether the deactivate question is shown.</summary>
    public bool IsConfirming { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<RouteDetails> api = await _handler.GetRouteAsync(RouteId, cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = AlertFor(api.Problem!);
            return;
        }

        Route = api.Value;
        IsConfirming = Deactivate && Route!.IsActive;
    }

    public async Task<IActionResult> OnPostDeactivateAsync(CancellationToken cancellationToken)
    {
        ApiResult<RouteDetails> api = await _handler.DeactivateRouteAsync(RouteId, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = Messages.SuccessDeactivated(EntityLabel);
            return RedirectToPage(RoutePage, new { routeId = RouteId });
        }

        Alert = AlertFor(api.Problem!);
        if (FleetDisplay.HidesRecord(api.Problem!))
        {
            return Page();
        }

        // Nothing changed (Fleet was down or failed): the route is shown again with the question
        // still open, so the operator can simply try again.
        ApiResult<RouteDetails> reload = await _handler.GetRouteAsync(RouteId, cancellationToken);
        if (reload.IsSuccess)
        {
            Route = reload.Value;
            IsConfirming = Route!.IsActive;
        }

        return Page();
    }

    /// <summary>The route page's own address: the Cancel link, and where the question posts from.</summary>
    public string RouteAddress()
    {
        return Url.Page(RoutePage, new { routeId = RouteId })!;
    }

    /// <summary>The route page address that opens the deactivate question.</summary>
    public string ConfirmAddress()
    {
        return Url.Page(RoutePage, new { routeId = RouteId, deactivate = true })!;
    }

    /// <summary>Where the question posts.</summary>
    public string DeactivateAddress()
    {
        return Url.Page(RoutePage, "Deactivate", new { routeId = RouteId })!;
    }

    /// <summary>The schedule form with this route already chosen.</summary>
    public string NewScheduleAddress()
    {
        return "/operator/schedules/new?routeId=" + RouteId;
    }

    private PageAlert AlertFor(ApiProblem problem)
    {
        PageAlert alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
        if (problem.Code == FleetDisplay.NotFound)
        {
            return alert.WithLink(BackToListText, RouteListAddress);
        }

        return alert;
    }
}
