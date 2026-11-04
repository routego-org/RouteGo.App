using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Operator.Routes;

/// <summary>
/// The list's status filter and page, kept in the page address so a reload or a shared link shows
/// the same list (?status=active&amp;pageNumber=2).
/// </summary>
public sealed class RoutesQuery
{
    /// <summary>active or inactive; empty means every route.</summary>
    public string? Status { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;
}

/// <summary>A route stop's city, by the name the route's name is made from.</summary>
public sealed record StopCity(string Name);

/// <summary>One stop of a route, in order.</summary>
public sealed record RouteStop(int Sequence, StopCity City);

/// <summary>The fields of one of the operator's routes the list shows.</summary>
public sealed record RouteSummary
{
    public required Guid RouteId { get; init; }

    /// <summary>The stops in order, the first city first.</summary>
    public required IReadOnlyList<RouteStop> Stops { get; init; }

    public required int DistanceKm { get; init; }

    public required int JourneyMinutes { get; init; }

    /// <summary>active or inactive.</summary>
    public required string Status { get; init; }

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

/// <summary>One page of the operator's routes, with how many match across all pages.</summary>
public sealed record RoutesPage(IReadOnlyList<RouteSummary> Items, int TotalCount, int Page);

public sealed class RoutesHandler
{
    private const string RoutesPath = "routes";

    private readonly IHttpClientFactory _httpClientFactory;

    public RoutesHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of the operator's routes in Fleet's order (first city, last city, when created).</summary>
    public async Task<ApiResult<RoutesPage>> ListRoutesAsync(RoutesQuery query, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        string address = QueryHelpers.AddQueryString(RoutesPath, ToListParameters(query));
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<RoutesPage>(cancellationToken);
    }

    // No status is sent for "All": Fleet then lists every route. The page and page size always are.
    private static Dictionary<string, string?> ToListParameters(RoutesQuery query)
    {
        var parameters = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(query.Status))
        {
            parameters["status"] = query.Status;
        }

        parameters["page"] = query.PageNumber.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = FleetDisplay.ListPageSize.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }
}

public sealed class RoutesModel : PageModel
{
    public const string NewRouteAddress = "/operator/routes/new";

    /// <summary>The filter's choices in the order offered: "All" (empty), then Fleet's statuses.</summary>
    public static readonly string[] Statuses = new[] { "", "active", "inactive" };

    private const string RoutesPage = "/Operator/Routes/Routes";
    private const string Items = "routes";

    private readonly RoutesHandler _handler;

    public RoutesModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new RoutesHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public RoutesQuery Query { get; set; } = new RoutesQuery();

    /// <summary>The loaded list; null when it could not be loaded.</summary>
    public RoutesPage? Routes { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the page shows only the alert: Fleet will not list this operator's routes yet or at all.</summary>
    public bool IsContentHidden { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<RoutesPage> api = await _handler.ListRoutesAsync(Query, cancellationToken);
        if (api.Errors != null)
        {
            // Fleet refused the filter in the address: the whole list is shown instead, with why.
            Alert = PageAlert.Danger(api.ErrorsTitle!);
            Query = new RoutesQuery();
            api = await _handler.ListRoutesAsync(Query, cancellationToken);
        }

        if (api.IsSuccess)
        {
            Routes = api.Value;
            return;
        }

        if (api.Problem != null)
        {
            Alert = FleetDisplay.ToAlert(api.Problem, Items, CorrelationId.Get(HttpContext));
            IsContentHidden = FleetDisplay.HidesRecord(api.Problem);
        }
    }

    /// <summary>The list address with the current filter and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(RoutesPage, new { status = Query.Status, pageNumber = page })!;
    }

    /// <summary>One route's page.</summary>
    public static string RouteAddress(Guid routeId)
    {
        return "/operator/routes/" + routeId;
    }

    /// <summary>The filter's word for a status: "All" for none, otherwise the catalog's state word.</summary>
    public static string StatusChoice(string status)
    {
        if (status.Length == 0)
        {
            return "All";
        }

        return FleetDisplay.StatusLabel(status);
    }

    /// <summary>Whether no status is chosen, so an empty list means the operator has no routes at all.</summary>
    public bool IsUnfiltered()
    {
        return string.IsNullOrEmpty(Query.Status);
    }
}
