using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.Trips;

/// <summary>
/// The search's filters and page, kept in the page address so a reload or a link from the
/// operators page shows the same list
/// (?operatorId=…&amp;operatorName=…&amp;sourceCityId=…&amp;destinationCityId=…&amp;from=…&amp;to=…&amp;status=…&amp;pageNumber=2).
/// </summary>
public sealed class TripSearchQuery
{
    /// <summary>Set only by an operators page "See trips" link: the one company searched.</summary>
    public string? OperatorId { get; set; }

    /// <summary>The company's name from the operators page; used only to show which company is chosen.</summary>
    public string? OperatorName { get; set; }

    /// <summary>Only trips whose route starts at this city.</summary>
    public Guid? SourceCityId { get; set; }

    /// <summary>Only trips whose route ends at this city.</summary>
    public Guid? DestinationCityId { get; set; }

    /// <summary>The first travel date (IST), yyyy-MM-dd as typed; empty means today.</summary>
    public string? From { get; set; }

    /// <summary>The last travel date (IST), yyyy-MM-dd as typed; empty means 13 days after From.</summary>
    public string? To { get; set; }

    /// <summary>scheduled, departed or cancelled; empty means every trip.</summary>
    public string? Status { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;

    public bool HasCompany()
    {
        return !string.IsNullOrEmpty(OperatorId);
    }
}

/// <summary>A city at one end of a trip's route.</summary>
public sealed record TripEnd(Guid CityId, string Name);

/// <summary>One trip of any company as Fleet's admin search returns it.</summary>
public sealed record FoundTrip
{
    public required Guid TripId { get; init; }

    /// <summary>The company's name.</summary>
    public required string OperatorName { get; init; }

    public required TripEnd Source { get; init; }

    public required TripEnd Destination { get; init; }

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

    /// <summary>"{first city} → {last city}".</summary>
    public string RouteName()
    {
        return Source.Name + " → " + Destination.Name;
    }
}

/// <summary>One page of matching trips, with how many match across all pages.</summary>
public sealed record FoundTripsPage(IReadOnlyList<FoundTrip> Items, int TotalCount, int Page);

/// <summary>A city from Fleet's city list, for the From city and To city filters.</summary>
public sealed record SearchCity(Guid CityId, string Name, string State)
{
    /// <summary>How the city is offered: "{city name}, {state}".</summary>
    public string Label()
    {
        return Name + ", " + State;
    }
}

/// <summary>Fleet's whole city list.</summary>
public sealed record SearchCityList(IReadOnlyList<SearchCity> Items);

public sealed class TripSearchHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TripSearchHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of every company's trips on the travel dates, in Fleet's order (by departure).</summary>
    public async Task<ApiResult<FoundTripsPage>> SearchTripsAsync(TripSearchQuery query, TripDates dates, CancellationToken cancellationToken)
    {
        string address = QueryHelpers.AddQueryString("admin/trips", ToSearchParameters(query, dates));
        return await GetAsync<FoundTripsPage>(address, cancellationToken);
    }

    /// <summary>Every city, sorted by name (then state, for two cities of the same name).</summary>
    public async Task<ApiResult<SearchCityList>> ListCitiesAsync(CancellationToken cancellationToken)
    {
        ApiResult<SearchCityList> api = await GetAsync<SearchCityList>("cities", cancellationToken);
        if (!api.IsSuccess)
        {
            return api;
        }

        IReadOnlyList<SearchCity> cities = api.Value!.Items;
        var sorted = cities.OrderBy(city => city.Name).ThenBy(city => city.State).ToList();
        return ApiResult<SearchCityList>.Success(new SearchCityList(sorted));
    }

    private async Task<ApiResult<T>> GetAsync<T>(string address, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<T>(cancellationToken);
    }

    // The dates are always sent, so the list Fleet makes is the one the filters show.
    private static Dictionary<string, string?> ToSearchParameters(TripSearchQuery query, TripDates dates)
    {
        var parameters = new Dictionary<string, string?>();
        if (query.HasCompany())
        {
            parameters["operatorId"] = query.OperatorId;
        }

        if (query.SourceCityId != null)
        {
            parameters["sourceCityId"] = query.SourceCityId.Value.ToString();
        }

        if (query.DestinationCityId != null)
        {
            parameters["destinationCityId"] = query.DestinationCityId.Value.ToString();
        }

        parameters["from"] = TripDisplay.WireDate(dates.From);
        parameters["to"] = TripDisplay.WireDate(dates.To);
        if (!string.IsNullOrEmpty(query.Status))
        {
            parameters["status"] = query.Status;
        }

        parameters["page"] = query.PageNumber.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = FleetDisplay.ListPageSize.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }
}
