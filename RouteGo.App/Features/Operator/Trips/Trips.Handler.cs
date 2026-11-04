using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Operator.Trips;

/// <summary>
/// The list's filters and page, kept in the page address so a reload or a link from a schedule page
/// shows the same list (?from=2026-10-12&amp;to=2026-10-18&amp;status=cancelled&amp;busId=…&amp;scheduleId=…&amp;pageNumber=2).
/// </summary>
public sealed class TripsQuery
{
    /// <summary>The first travel date (IST), yyyy-MM-dd as typed; empty means today.</summary>
    public string? From { get; set; }

    /// <summary>The last travel date (IST), yyyy-MM-dd as typed; empty means 13 days after From.</summary>
    public string? To { get; set; }

    /// <summary>scheduled, departed or cancelled; empty means every trip.</summary>
    public string? Status { get; set; }

    public Guid? BusId { get; set; }

    /// <summary>Set only by the "See trips" link of a schedule page.</summary>
    public Guid? ScheduleId { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;
}

/// <summary>A city at one end of a trip's route.</summary>
public sealed record TripCity(Guid CityId, string Name);

/// <summary>One of the operator's trips as Fleet returns it.</summary>
public sealed record TripDetails
{
    public required Guid TripId { get; init; }

    public required TripCity Source { get; init; }

    public required TripCity Destination { get; init; }

    public required Guid BusId { get; init; }

    public required string BusName { get; init; }

    /// <summary>The calendar date (IST) the trip leaves on.</summary>
    public required DateOnly ServiceDate { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    public required DateTimeOffset ArrivalAt { get; init; }

    /// <summary>scheduled, departed or cancelled.</summary>
    public required string Status { get; init; }

    public required IReadOnlyList<ClassFare> ClassFares { get; init; }

    public required string? CancelReason { get; init; }

    public required DateTimeOffset? CancelledAt { get; init; }

    public bool IsScheduled
    {
        get { return Status == "scheduled"; }
    }

    /// <summary>"{first city} → {last city}".</summary>
    public string RouteName()
    {
        return Source.Name + " → " + Destination.Name;
    }
}

/// <summary>One page of the operator's trips, with how many match across all pages.</summary>
public sealed record TripsPage(IReadOnlyList<TripDetails> Items, int TotalCount, int Page);

/// <summary>One of the operator's buses, for the Bus filter.</summary>
public sealed record TripBus(Guid BusId, string Name);

/// <summary>One page of the operator's buses.</summary>
public sealed record TripBusList(IReadOnlyList<TripBus> Items);

public sealed record TripFaresRequest(IReadOnlyList<ClassFare> ClassFares);

public sealed record TripCancellation(string Reason);

public sealed class TripsHandler
{
    /// <summary>Fleet's largest page: the Bus filter needs every one of the operator's buses.</summary>
    private const string BusPageSize = "200";

    private readonly IHttpClientFactory _httpClientFactory;

    public TripsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of the operator's trips on the travel dates, in Fleet's order (by departure).</summary>
    public async Task<ApiResult<TripsPage>> ListTripsAsync(TripsQuery query, TripDates dates, CancellationToken cancellationToken)
    {
        string address = QueryHelpers.AddQueryString("trips", ToListParameters(query, dates));
        return await SendAsync<TripsPage>(HttpMethod.Get, address, null, cancellationToken);
    }

    /// <summary>Every bus of the operator, for the Bus filter.</summary>
    public async Task<ApiResult<TripBusList>> ListBusesAsync(CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string?> { ["page"] = "1", ["pageSize"] = BusPageSize };
        string address = QueryHelpers.AddQueryString("buses", parameters);
        return await SendAsync<TripBusList>(HttpMethod.Get, address, null, cancellationToken);
    }

    /// <summary>Replaces one trip's fares; the schedule and its other trips keep theirs.</summary>
    public async Task<ApiResult<TripDetails>> ReplaceTripFaresAsync(Guid tripId, IReadOnlyList<ClassFare> fares, CancellationToken cancellationToken)
    {
        var body = new TripFaresRequest(fares);
        return await SendAsync<TripDetails>(HttpMethod.Put, $"trips/{tripId}/fares", body, cancellationToken);
    }

    /// <summary>Cancels one trip for good, with the operator's reason.</summary>
    public async Task<ApiResult<TripDetails>> CancelTripAsync(Guid tripId, string reason, CancellationToken cancellationToken)
    {
        var body = new TripCancellation(reason);
        return await SendAsync<TripDetails>(HttpMethod.Post, $"trips/{tripId}/cancel", body, cancellationToken);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string address, object? body, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(method, address);
        if (body != null)
        {
            request.Content = JsonContent.Create(body, options: WireJson.Options);
        }

        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<T>(cancellationToken);
    }

    // The dates are always sent, so the list Fleet makes is the one the filter shows.
    private static Dictionary<string, string?> ToListParameters(TripsQuery query, TripDates dates)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["from"] = TripDisplay.WireDate(dates.From),
            ["to"] = TripDisplay.WireDate(dates.To),
        };
        if (query.ScheduleId != null)
        {
            parameters["scheduleId"] = query.ScheduleId.Value.ToString();
        }

        if (query.BusId != null)
        {
            parameters["busId"] = query.BusId.Value.ToString();
        }

        if (!string.IsNullOrEmpty(query.Status))
        {
            parameters["status"] = query.Status;
        }

        parameters["page"] = query.PageNumber.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = FleetDisplay.ListPageSize.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }
}
