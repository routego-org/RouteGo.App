using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Common.UI;

/// <summary>A route stop's city, by the name the route's name is made from.</summary>
public sealed record ScheduleFormStopCity(string Name);

/// <summary>One stop of a route, in order.</summary>
public sealed record ScheduleFormStop(int Sequence, ScheduleFormStopCity City);

/// <summary>The fields of one of the operator's routes the form uses.</summary>
public sealed record ScheduleFormRoute(Guid RouteId, IReadOnlyList<ScheduleFormStop> Stops, string Status);

/// <summary>One page of the operator's routes.</summary>
public sealed record ScheduleFormRouteList(IReadOnlyList<ScheduleFormRoute> Items);

/// <summary>The fields of one of the operator's buses the form uses.</summary>
public sealed record ScheduleFormBus(Guid BusId, string Name, string Status);

/// <summary>One page of the operator's buses.</summary>
public sealed record ScheduleFormBusList(IReadOnlyList<ScheduleFormBus> Items);

/// <summary>A bus's seat classes in its order: one fare box each.</summary>
public sealed record ScheduleFormSeatClasses(IReadOnlyList<string> SeatClasses);

/// <summary>
/// The Fleet reads both schedule form pages need: the operator's routes and buses to choose from,
/// and the chosen bus's seat classes.
/// </summary>
public sealed class ScheduleFormFleet
{
    public const string ActiveStatus = "active";

    /// <summary>Fleet's largest page: the choices need every one of the operator's routes and buses.</summary>
    private const string ChoicePageSize = "200";

    private readonly IHttpClientFactory _httpClientFactory;

    public ScheduleFormFleet(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The operator's routes as choices, named by their cities; <paramref name="status"/> "active" or null for every route.</summary>
    public async Task<ApiResult<IReadOnlyList<ScheduleFormChoice>>> ListRoutesAsync(string? status, CancellationToken cancellationToken)
    {
        ApiResult<ScheduleFormRouteList> api = await GetAsync<ScheduleFormRouteList>(ChoiceAddress("routes", status), cancellationToken);
        if (!api.IsSuccess)
        {
            return new ApiResult<IReadOnlyList<ScheduleFormChoice>>(null, api.Errors, api.Problem);
        }

        var choices = new List<ScheduleFormChoice>();
        foreach (ScheduleFormRoute route in api.Value!.Items)
        {
            choices.Add(new ScheduleFormChoice(route.RouteId, RouteName(route), route.Status == ActiveStatus));
        }

        return ApiResult<IReadOnlyList<ScheduleFormChoice>>.Success(choices);
    }

    /// <summary>The operator's buses as choices, by name; <paramref name="status"/> "active" or null for every bus.</summary>
    public async Task<ApiResult<IReadOnlyList<ScheduleFormChoice>>> ListBusesAsync(string? status, CancellationToken cancellationToken)
    {
        ApiResult<ScheduleFormBusList> api = await GetAsync<ScheduleFormBusList>(ChoiceAddress("buses", status), cancellationToken);
        if (!api.IsSuccess)
        {
            return new ApiResult<IReadOnlyList<ScheduleFormChoice>>(null, api.Errors, api.Problem);
        }

        var choices = new List<ScheduleFormChoice>();
        foreach (ScheduleFormBus bus in api.Value!.Items)
        {
            choices.Add(new ScheduleFormChoice(bus.BusId, bus.Name, bus.Status == ActiveStatus));
        }

        return ApiResult<IReadOnlyList<ScheduleFormChoice>>.Success(choices);
    }

    /// <summary>The bus's seat classes in its order.</summary>
    public async Task<ApiResult<ScheduleFormSeatClasses>> GetSeatClassesAsync(Guid busId, CancellationToken cancellationToken)
    {
        return await GetAsync<ScheduleFormSeatClasses>($"buses/{busId}", cancellationToken);
    }

    private async Task<ApiResult<T>> GetAsync<T>(string address, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<T>(cancellationToken);
    }

    private static string ChoiceAddress(string path, string? status)
    {
        var parameters = new Dictionary<string, string?>();
        if (status != null)
        {
            parameters["status"] = status;
        }

        parameters["page"] = "1";
        parameters["pageSize"] = ChoicePageSize;
        return QueryHelpers.AddQueryString(path, parameters);
    }

    private static string RouteName(ScheduleFormRoute route)
    {
        var cityNames = new List<string>();
        foreach (ScheduleFormStop stop in route.Stops)
        {
            cityNames.Add(stop.City.Name);
        }

        return FleetDisplay.RouteName(cityNames);
    }
}
