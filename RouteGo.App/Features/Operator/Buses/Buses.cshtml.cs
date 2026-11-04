using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Operator.Buses;

/// <summary>
/// The list's status filter and page, kept in the page address so a reload or a shared link shows
/// the same list (?status=inactive&amp;pageNumber=2).
/// </summary>
public sealed class BusesQuery
{
    /// <summary>active or inactive; empty means every bus.</summary>
    public string? Status { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;
}

/// <summary>One of the operator's buses as the list shows it.</summary>
public sealed record BusSummary
{
    public required Guid BusId { get; init; }

    public required string RegistrationNumber { get; init; }

    public required string Name { get; init; }

    /// <summary>seater, sleeper or semiSleeper.</summary>
    public required string BusType { get; init; }

    public required bool IsAc { get; init; }

    public required int TotalSeats { get; init; }

    /// <summary>active or inactive.</summary>
    public required string Status { get; init; }

    /// <summary>The photo storage address of the first photo; null when the bus has none.</summary>
    public required string? CoverPhotoUrl { get; init; }
}

/// <summary>One page of the operator's buses, with how many match across all pages.</summary>
public sealed record BusesPage(IReadOnlyList<BusSummary> Items, int TotalCount, int Page);

public sealed class BusesHandler
{
    private const string BusesPath = "buses";

    private readonly IHttpClientFactory _httpClientFactory;

    public BusesHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of the operator's buses in Fleet's order (by name).</summary>
    public async Task<ApiResult<BusesPage>> ListBusesAsync(BusesQuery query, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        string address = QueryHelpers.AddQueryString(BusesPath, ToListParameters(query));
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<BusesPage>(cancellationToken);
    }

    // No status is sent for "All": Fleet then lists every bus. The page and page size always are.
    private static Dictionary<string, string?> ToListParameters(BusesQuery query)
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

public sealed class BusesModel : PageModel
{
    public const string NewBusAddress = "/operator/buses/new";

    /// <summary>The filter's choices in the order offered: "All" (empty), then Fleet's statuses.</summary>
    public static readonly string[] Statuses = new[] { "", "active", "inactive" };

    private const string BusesPage = "/Operator/Buses/Buses";
    private const string Items = "buses";

    private readonly BusesHandler _handler;

    public BusesModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new BusesHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public BusesQuery Query { get; set; } = new BusesQuery();

    /// <summary>The loaded list; null when it could not be loaded.</summary>
    public BusesPage? Buses { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the page shows only the alert: Fleet will not list this operator's buses yet or at all.</summary>
    public bool IsContentHidden { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<BusesPage> api = await _handler.ListBusesAsync(Query, cancellationToken);
        if (api.Errors != null)
        {
            // Fleet refused the filter in the address: the whole list is shown instead, with why.
            Alert = PageAlert.Danger(api.ErrorsTitle!);
            Query = new BusesQuery();
            api = await _handler.ListBusesAsync(Query, cancellationToken);
        }

        if (api.IsSuccess)
        {
            Buses = api.Value;
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
        return Url.Page(BusesPage, new { status = Query.Status, pageNumber = page })!;
    }

    /// <summary>One bus's page.</summary>
    public static string BusAddress(Guid busId)
    {
        return "/operator/buses/" + busId;
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

    /// <summary>Whether no status is chosen, so an empty list means the operator has no buses at all.</summary>
    public bool IsUnfiltered()
    {
        return string.IsNullOrEmpty(Query.Status);
    }
}
