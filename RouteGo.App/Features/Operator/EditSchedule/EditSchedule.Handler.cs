namespace RouteGo.App.Features.Operator.EditSchedule;

/// <summary>The fields of one of the operator's schedules the form is filled with and compared against.</summary>
public sealed record SavedSchedule
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

    public bool IsActive
    {
        get { return Status == "active"; }
    }
}

/// <summary>
/// The routes and buses the schedule may use: the operator's active ones, plus the schedule's own
/// route and bus even when they are no longer active (marked so on the form).
/// </summary>
public sealed record EditScheduleChoices(IReadOnlyList<ScheduleFormChoice> Routes, IReadOnlyList<ScheduleFormChoice> Buses);

/// <summary>How Fleet's replacement changed the schedule's trips.</summary>
public sealed record ScheduleChange(int CreatedTripCount, int CancelledTripCount, int RepricedTripCount)
{
    /// <summary>What the schedule page says after saving: the three counts, or that no trip changed.</summary>
    public string SavedText()
    {
        if (CreatedTripCount == 0 && CancelledTripCount == 0 && RepricedTripCount == 0)
        {
            return "Schedule saved. No trips needed to change.";
        }

        return $"Schedule saved. {CreatedTripCount} new trips, {CancelledTripCount} trips cancelled, {RepricedTripCount} trips with new fares.";
    }
}

public sealed class EditScheduleHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ScheduleFormFleet _fleet;

    public EditScheduleHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _fleet = new ScheduleFormFleet(httpClientFactory);
    }

    /// <summary>The schedule as saved.</summary>
    public async Task<ApiResult<SavedSchedule>> GetScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Get, $"schedules/{scheduleId}");
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<SavedSchedule>(cancellationToken);
    }

    /// <summary>
    /// Every route and bus of the operator, kept when active or when the schedule uses it; the first
    /// refusal of either list is the answer.
    /// </summary>
    public async Task<ApiResult<EditScheduleChoices>> ListChoicesAsync(SavedSchedule schedule, CancellationToken cancellationToken)
    {
        Task<ApiResult<IReadOnlyList<ScheduleFormChoice>>> routes = _fleet.ListRoutesAsync(null, cancellationToken);
        Task<ApiResult<IReadOnlyList<ScheduleFormChoice>>> buses = _fleet.ListBusesAsync(null, cancellationToken);
        await Task.WhenAll(routes, buses);
        ApiResult<IReadOnlyList<ScheduleFormChoice>> routeAnswer = await routes;
        ApiResult<IReadOnlyList<ScheduleFormChoice>> busAnswer = await buses;
        if (!routeAnswer.IsSuccess)
        {
            return new ApiResult<EditScheduleChoices>(null, routeAnswer.Errors, routeAnswer.Problem);
        }

        if (!busAnswer.IsSuccess)
        {
            return new ApiResult<EditScheduleChoices>(null, busAnswer.Errors, busAnswer.Problem);
        }

        IReadOnlyList<ScheduleFormChoice> usableRoutes = Usable(routeAnswer.Value!, schedule.RouteId);
        IReadOnlyList<ScheduleFormChoice> usableBuses = Usable(busAnswer.Value!, schedule.BusId);
        return ApiResult<EditScheduleChoices>.Success(new EditScheduleChoices(usableRoutes, usableBuses));
    }

    /// <summary>The chosen bus's seat classes, one fare box each.</summary>
    public async Task<ApiResult<ScheduleFormSeatClasses>> GetSeatClassesAsync(Guid busId, CancellationToken cancellationToken)
    {
        return await _fleet.GetSeatClassesAsync(busId, cancellationToken);
    }

    /// <summary>
    /// Replaces every detail of the schedule. Fleet cancels upcoming trips whose route, bus, time or
    /// day no longer fit, makes new ones, and re-prices the rest.
    /// </summary>
    public async Task<ApiResult<ScheduleChange>> ReplaceScheduleAsync(Guid scheduleId, ScheduleRequest schedule, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Put, $"schedules/{scheduleId}")
        {
            Content = JsonContent.Create(schedule, options: WireJson.Options),
        };
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<ScheduleChange>(cancellationToken);
    }

    private static IReadOnlyList<ScheduleFormChoice> Usable(IReadOnlyList<ScheduleFormChoice> choices, Guid currentId)
    {
        return choices.Where(choice => choice.IsActive || choice.Id == currentId).ToList();
    }
}
