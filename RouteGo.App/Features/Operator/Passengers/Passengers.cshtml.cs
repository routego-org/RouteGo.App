namespace RouteGo.App.Features.Operator.Passengers;

public sealed class PassengersModel : PageModel
{
    public const string MyTripsAddress = "/operator/trips";
    public const string BackToTrips = "Back to trips";
    public const string NoPassengers = "No seats are booked on this trip yet.";
    public const string NotAvailableYet = "Passenger details for this trip are not available yet. If the trip was put on sale just now, try again in a minute.";

    private const string PassengersPage = "/Operator/Passengers/Passengers";
    private const string NotFoundCode = "NOT_FOUND";

    private readonly PassengersHandler _handler;

    public PassengersModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new PassengersHandler(httpClientFactory);
    }

    /// <summary>The trip named in the address, as typed: an address that names no trip is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? TripId { get; set; }

    /// <summary>The route label the "Passengers" link passed ("Mumbai → Pune"); only shown in the heading.</summary>
    [BindProperty(SupportsGet = true, Name = "route")]
    public string? RouteName { get; set; }

    /// <summary>The loaded passengers; null when they could not be loaded.</summary>
    public TripPassengerList? Passengers { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(TripId, out Guid tripId))
        {
            Alert = NotAvailableAlert();
            return;
        }

        ApiResult<TripPassengerList> api = await _handler.ListPassengersAsync(tripId, cancellationToken);
        if (api.IsSuccess)
        {
            Passengers = api.Value;
            return;
        }

        Alert = ToAlert(api);
    }

    /// <summary>"Passengers: Mumbai → Pune, 30 Oct 2026, 9:00 PM IST".</summary>
    public string Heading()
    {
        return PassengersHandler.Heading(RouteName, Passengers!.Trip.DepartureAt);
    }

    public string SeatsBookedText()
    {
        return PassengersHandler.SeatsBookedText(Passengers!.SeatsBooked);
    }

    // A trip Booking does not know yet (or an address that names no trip) is told as "not yet";
    // another company's trip and any other refusal show no passengers at all.
    private PageAlert ToAlert(ApiResult<TripPassengerList> api)
    {
        ApiProblem? problem = api.Problem;
        if (api.Errors != null || (problem != null && problem.Code == NotFoundCode))
        {
            return NotAvailableAlert();
        }

        if (problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(CorrelationId.Get(HttpContext)));
        }

        PageAlert alert = SharedProblems.ToAlert(problem, CorrelationId.Get(HttpContext));
        if (problem.Code == SharedProblems.Forbidden)
        {
            return alert.WithLink(BackToTrips, MyTripsAddress);
        }

        return alert;
    }

    private PageAlert NotAvailableAlert()
    {
        string sameAddress = Url.Page(PassengersPage, new RouteValueDictionary { ["tripId"] = TripId, ["route"] = RouteName })!;
        var alert = PageAlert.Danger(NotAvailableYet);
        return alert.WithLink(Messages.LinkTryAgain, sameAddress).WithLink(BackToTrips, MyTripsAddress);
    }
}
