using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Operator.Schedules;

/// <summary>
/// The list's filters and page, kept in the page address so a reload or a link from a bus page shows
/// the same list (?status=active&amp;routeId=…&amp;busId=…&amp;pageNumber=2).
/// </summary>
public sealed class SchedulesQuery
{
    /// <summary>active or inactive; empty means every schedule.</summary>
    public string? Status { get; set; }

    public Guid? RouteId { get; set; }

    public Guid? BusId { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>Whether no filter is chosen, so an empty list means the operator has no schedules at all.</summary>
    public bool IsUnfiltered()
    {
        return string.IsNullOrEmpty(Status) && RouteId is null && BusId is null;
    }
}

/// <summary>The fields of one of the operator's schedules the list shows.</summary>
public sealed record ScheduleSummary
{
    public required Guid ScheduleId { get; init; }

    public required Guid RouteId { get; init; }

    public required Guid BusId { get; init; }

    /// <summary>HH:mm on the 24-hour clock, IST.</summary>
    public required string DepartureTime { get; init; }

    public required IReadOnlyList<string> DaysOfWeek { get; init; }

    public required IReadOnlyList<ClassFare> ClassFares { get; init; }

    /// <summary>active or inactive.</summary>
    public required string Status { get; init; }

    public required DateOnly TripsGeneratedThrough { get; init; }
}

/// <summary>One page of the operator's schedules, with how many match across all pages.</summary>
public sealed record SchedulesPage(IReadOnlyList<ScheduleSummary> Items, int TotalCount, int Page);

/// <summary>A route stop's city, by the name the route's name is made from.</summary>
public sealed record StopCity(string Name);

/// <summary>One stop of a route, in order.</summary>
public sealed record RouteStop(int Sequence, StopCity City);

/// <summary>One of the operator's routes, for its name.</summary>
public sealed record RouteSummary(Guid RouteId, IReadOnlyList<RouteStop> Stops)
{
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

/// <summary>One of the operator's buses, for its name.</summary>
public sealed record BusSummary(Guid BusId, string Name);

/// <summary>One page of the operator's routes.</summary>
public sealed record RouteList(IReadOnlyList<RouteSummary> Items);

/// <summary>One page of the operator's buses.</summary>
public sealed record BusList(IReadOnlyList<BusSummary> Items);

/// <summary>
/// The schedules with the operator's routes and buses for their names. A route or bus list Fleet
/// would not give is null: the schedules still show, and that filter is not offered.
/// </summary>
public sealed record SchedulesPageData(SchedulesPage Schedules, IReadOnlyList<RouteSummary>? Routes, IReadOnlyList<BusSummary>? Buses);

public sealed class SchedulesHandler
{
    /// <summary>Fleet's largest page: the route and bus filters need every one of the operator's routes and buses.</summary>
    private const string ChoicePageSize = "200";

    private readonly IHttpClientFactory _httpClientFactory;

    public SchedulesHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of schedules in Fleet's order (by departure time), then the routes and buses for their names.</summary>
    public async Task<ApiResult<SchedulesPageData>> ListSchedulesAsync(SchedulesQuery query, CancellationToken cancellationToken)
    {
        string schedulesAddress = QueryHelpers.AddQueryString("schedules", ToListParameters(query));
        ApiResult<SchedulesPage> schedules = await GetAsync<SchedulesPage>(schedulesAddress, cancellationToken);
        if (!schedules.IsSuccess)
        {
            return new ApiResult<SchedulesPageData>(null, schedules.Errors, schedules.Problem) { ErrorsTitle = schedules.ErrorsTitle };
        }

        Task<ApiResult<RouteList>> routes = GetAsync<RouteList>(ChoiceAddress("routes"), cancellationToken);
        Task<ApiResult<BusList>> buses = GetAsync<BusList>(ChoiceAddress("buses"), cancellationToken);
        await Task.WhenAll(routes, buses);
        RouteList? routeList = (await routes).Value;
        BusList? busList = (await buses).Value;

        var data = new SchedulesPageData(schedules.Value!, routeList?.Items, busList?.Items);
        return ApiResult<SchedulesPageData>.Success(data);
    }

    private async Task<ApiResult<T>> GetAsync<T>(string address, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<T>(cancellationToken);
    }

    private static string ChoiceAddress(string path)
    {
        var parameters = new Dictionary<string, string?> { ["page"] = "1", ["pageSize"] = ChoicePageSize };
        return QueryHelpers.AddQueryString(path, parameters);
    }

    // Only the chosen filters are sent ("All" sends none). The page and page size always are.
    private static Dictionary<string, string?> ToListParameters(SchedulesQuery query)
    {
        var parameters = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(query.Status))
        {
            parameters["status"] = query.Status;
        }

        if (query.RouteId != null)
        {
            parameters["routeId"] = query.RouteId.Value.ToString();
        }

        if (query.BusId != null)
        {
            parameters["busId"] = query.BusId.Value.ToString();
        }

        parameters["page"] = query.PageNumber.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = FleetDisplay.ListPageSize.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }
}
