namespace RouteGo.App.Features.Operator.Schedules;

public sealed class SchedulesModel : PageModel
{
    public const string NewScheduleAddress = "/operator/schedules/new";

    /// <summary>The status filter's choices in the order offered: "All" (empty), then Fleet's statuses.</summary>
    public static readonly string[] Statuses = new[] { "", "active", "inactive" };

    private const string SchedulesPage = "/Operator/Schedules/Schedules";
    private const string Items = "schedules";

    private readonly SchedulesHandler _handler;

    public SchedulesModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new SchedulesHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public SchedulesQuery Query { get; set; } = new SchedulesQuery();

    /// <summary>The loaded list with the route and bus names; null when it could not be loaded.</summary>
    public SchedulesPageData? Data { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the page shows only the alert: Fleet will not list this operator's schedules yet or at all.</summary>
    public bool IsContentHidden { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<SchedulesPageData> api = await _handler.ListSchedulesAsync(Query, cancellationToken);
        if (api.Errors != null)
        {
            // Fleet refused a filter in the address: the whole list is shown instead, with why.
            Alert = PageAlert.Danger(api.ErrorsTitle!);
            Query = new SchedulesQuery();
            api = await _handler.ListSchedulesAsync(Query, cancellationToken);
        }

        if (api.IsSuccess)
        {
            Data = api.Value;
            return;
        }

        if (api.Problem != null)
        {
            Alert = FleetDisplay.ToAlert(api.Problem, Items, CorrelationId.Get(HttpContext));
            IsContentHidden = FleetDisplay.HidesRecord(api.Problem);
        }
    }

    /// <summary>The list address with the current filters and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(SchedulesPage, new { status = Query.Status, routeId = Query.RouteId, busId = Query.BusId, pageNumber = page })!;
    }

    /// <summary>One schedule's page.</summary>
    public static string ScheduleAddress(Guid scheduleId)
    {
        return "/operator/schedules/" + scheduleId;
    }

    /// <summary>The status filter's word for a status: "All" for none, otherwise the catalog's state word.</summary>
    public static string StatusChoice(string status)
    {
        if (status.Length == 0)
        {
            return "All";
        }

        return FleetDisplay.StatusLabel(status);
    }

    /// <summary>The route's name, or "Unknown route" when the operator's routes did not include it.</summary>
    public string RouteName(Guid routeId)
    {
        RouteSummary? route = Data!.Routes?.FirstOrDefault(candidate => candidate.RouteId == routeId);
        if (route is null)
        {
            return TripDisplay.UnknownRoute;
        }

        return route.Name();
    }

    /// <summary>The bus's name, or "Unknown bus" when the operator's buses did not include it.</summary>
    public string BusName(Guid busId)
    {
        BusSummary? bus = Data!.Buses?.FirstOrDefault(candidate => candidate.BusId == busId);
        if (bus is null)
        {
            return TripDisplay.UnknownBus;
        }

        return bus.Name;
    }
}
