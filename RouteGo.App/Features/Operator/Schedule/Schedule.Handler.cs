namespace RouteGo.App.Features.Operator.Schedule;

/// <summary>One of the operator's schedules as Fleet returns it.</summary>
public sealed record ScheduleDetails
{
    public required Guid ScheduleId { get; init; }

    public required Guid RouteId { get; init; }

    public required Guid BusId { get; init; }

    /// <summary>HH:mm on the 24-hour clock, IST.</summary>
    public required string DepartureTime { get; init; }

    /// <summary>Fleet's day words ("monday").</summary>
    public required IReadOnlyList<string> DaysOfWeek { get; init; }

    public required IReadOnlyList<ClassFare> ClassFares { get; init; }

    /// <summary>active or inactive.</summary>
    public required string Status { get; init; }

    /// <summary>The last travel date the schedule's trips are on sale for.</summary>
    public required DateOnly TripsGeneratedThrough { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool IsActive
    {
        get { return Status == "active"; }
    }
}

/// <summary>A route stop's city, by the name the page shows.</summary>
public sealed record ScheduleStopCity(string Name);

/// <summary>One stop of the schedule's route: its city and minutes after leaving the first stop.</summary>
public sealed record ScheduleRouteStop(int Sequence, ScheduleStopCity City, int OffsetMinutes);

/// <summary>The fields of the schedule's route the page shows.</summary>
public sealed record ScheduleRoute(Guid RouteId, IReadOnlyList<ScheduleRouteStop> Stops)
{
    /// <summary>The route's name, made from its cities in stop order.</summary>
    public string Name()
    {
        var cityNames = new List<string>();
        foreach (ScheduleRouteStop stop in Stops)
        {
            cityNames.Add(stop.City.Name);
        }

        return FleetDisplay.RouteName(cityNames);
    }
}

/// <summary>The fields of the schedule's bus the page shows: its name and its seat classes in order.</summary>
public sealed record ScheduleBus(Guid BusId, string Name, IReadOnlyList<string> SeatClasses);

/// <summary>
/// The schedule with its route and bus. The route or bus is null when Fleet would not give it; the
/// schedule is still shown, with "Unknown route" or "Unknown bus".
/// </summary>
public sealed record SchedulePageData(ScheduleDetails Schedule, ScheduleRoute? Route, ScheduleBus? Bus)
{
    /// <summary>"{route name}, {departure time}".</summary>
    public string Name()
    {
        string routeName = TripDisplay.UnknownRoute;
        if (Route != null)
        {
            routeName = Route.Name();
        }

        return TripDisplay.ScheduleName(routeName, Schedule.DepartureTime);
    }
}

public sealed record TripExtensionRequest(DateOnly EndDate);

/// <summary>One trip put on sale; only how many there were is shown.</summary>
public sealed record ExtendedTrip(Guid TripId);

/// <summary>What putting trips on sale made, and the last date now on sale.</summary>
public sealed record TripExtension(DateOnly TripsGeneratedThrough, IReadOnlyList<ExtendedTrip> Items);

/// <summary>The field of Fleet's answer to stopping a schedule this page uses.</summary>
public sealed record StoppedSchedule(int CancelledTripCount);

public sealed class ScheduleHandler
{
    private const string SchedulesPath = "schedules";

    private readonly IHttpClientFactory _httpClientFactory;

    public ScheduleHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The schedule, then its route and bus for their names; a refusal of the schedule itself is the page's answer.</summary>
    public async Task<ApiResult<SchedulePageData>> GetPageDataAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        ApiResult<ScheduleDetails> schedule = await SendAsync<ScheduleDetails>(HttpMethod.Get, $"{SchedulesPath}/{scheduleId}", null, cancellationToken);
        if (!schedule.IsSuccess)
        {
            return ApiResult<SchedulePageData>.Refused(schedule.Problem!);
        }

        ScheduleDetails details = schedule.Value!;
        Task<ApiResult<ScheduleRoute>> route = SendAsync<ScheduleRoute>(HttpMethod.Get, $"routes/{details.RouteId}", null, cancellationToken);
        Task<ApiResult<ScheduleBus>> bus = SendAsync<ScheduleBus>(HttpMethod.Get, $"buses/{details.BusId}", null, cancellationToken);
        await Task.WhenAll(route, bus);
        ApiResult<ScheduleRoute> routeAnswer = await route;
        ApiResult<ScheduleBus> busAnswer = await bus;

        var data = new SchedulePageData(details, routeAnswer.Value, busAnswer.Value);
        return ApiResult<SchedulePageData>.Success(data);
    }

    /// <summary>Puts the schedule's trips on sale up to the end date; dates already on sale or cancelled are skipped by Fleet.</summary>
    public async Task<ApiResult<TripExtension>> CreateTripsAsync(Guid scheduleId, DateOnly endDate, CancellationToken cancellationToken)
    {
        var body = new TripExtensionRequest(endDate);
        return await SendAsync<TripExtension>(HttpMethod.Post, $"{SchedulesPath}/{scheduleId}/trips", body, cancellationToken);
    }

    /// <summary>Stops the schedule and cancels every upcoming trip of it.</summary>
    public async Task<ApiResult<StoppedSchedule>> DeactivateScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        return await SendAsync<StoppedSchedule>(HttpMethod.Post, $"{SchedulesPath}/{scheduleId}/deactivate", null, cancellationToken);
    }

    /// <summary>Makes a stopped schedule usable again; it cancels nothing and puts nothing on sale.</summary>
    public async Task<ApiResult<ScheduleDetails>> ActivateScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        return await SendAsync<ScheduleDetails>(HttpMethod.Post, $"{SchedulesPath}/{scheduleId}/activate", null, cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: WireJson.Options);
        }

        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<T>(cancellationToken);
    }
}
