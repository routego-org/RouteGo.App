namespace RouteGo.App.Features.Operator.NewRoute;

/// <summary>A city from Fleet's city list.</summary>
public sealed record City(Guid CityId, string Name, string State)
{
    /// <summary>How the city is offered: "{city name}, {state}".</summary>
    public string Label()
    {
        return Name + ", " + State;
    }
}

/// <summary>Fleet's whole city list.</summary>
public sealed record CityList(IReadOnlyList<City> Items);

/// <summary>One stop as Fleet takes it: the city and minutes after leaving the first stop.</summary>
public sealed record NewRouteStopRequest(Guid CityId, int OffsetMinutes);

public sealed record NewRouteRequest(IReadOnlyList<NewRouteStopRequest> Stops, int DistanceKm);

/// <summary>The field of Fleet's new route this page uses.</summary>
public sealed record CreatedRoute(Guid RouteId);

public sealed class NewRouteHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NewRouteHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Every city a route can use, sorted by name (then state, for two cities of the same name).</summary>
    public async Task<ApiResult<CityList>> ListCitiesAsync(CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Get, "cities");
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        ApiResult<CityList> api = await response.ReadApiResultAsync<CityList>(cancellationToken);
        if (!api.IsSuccess)
        {
            return api;
        }

        IReadOnlyList<City> cities = api.Value!.Items;
        var sorted = cities.OrderBy(city => city.Name).ThenBy(city => city.State).ToList();
        return ApiResult<CityList>.Success(new CityList(sorted));
    }

    /// <summary>Asks Fleet to create the route. Fleet never lets it change afterwards.</summary>
    public async Task<ApiResult<CreatedRoute>> CreateRouteAsync(NewRouteInput input, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Post, "routes")
        {
            Content = JsonContent.Create(ToRequest(input), options: WireJson.Options),
        };
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<CreatedRoute>(cancellationToken);
    }

    // Called only after the page's checks passed, so every city is chosen and every number readable.
    private static NewRouteRequest ToRequest(NewRouteInput input)
    {
        var stops = new List<NewRouteStopRequest>();
        stops.Add(new NewRouteStopRequest(input.Stops[0].CityId!.Value, 0));
        for (int index = 1; index < input.Stops.Count; index++)
        {
            NewRouteStopInput stop = input.Stops[index];
            stops.Add(new NewRouteStopRequest(stop.CityId!.Value, stop.TotalMinutes()!.Value));
        }

        return new NewRouteRequest(stops, NewRouteInput.WholeNumber(input.DistanceKm)!.Value);
    }
}
