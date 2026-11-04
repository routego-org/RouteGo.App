using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Operator.Dashboard;

/// <summary>The operator's own sales figures for a range: the five totals and one entry for every day of it.</summary>
public sealed record OperatorSummary
{
    public required SalesTotals Totals { get; init; }

    public required IReadOnlyList<SalesDay> DailySeries { get; init; }
}

/// <summary>One of the operator's trips on a day and how many of its seats are sold.</summary>
public sealed record TripOccupancy
{
    public required string TripId { get; init; }

    public required string Source { get; init; }

    public required string Destination { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    /// <summary>Seats on bookings that are confirmed now.</summary>
    public required int SeatsSold { get; init; }

    public required int SeatsTotal { get; init; }
}

public sealed record OperatorTripList
{
    public required IReadOnlyList<TripOccupancy> Items { get; init; }
}

/// <summary>What the sales section shows: the figures, or why there are none; the checks on the typed dates sit beside the boxes.</summary>
public sealed record SalesSection
{
    public OperatorSummary? Summary { get; init; }

    public PageAlert? Alert { get; init; }

    public ReportRangeCheck Check { get; init; } = new ReportRangeCheck();
}

/// <summary>What the trips section shows: the trips of the day, or why there are none.</summary>
public sealed record TripsSection
{
    public OperatorTripList? Trips { get; init; }

    public PageAlert? Alert { get; init; }

    public string? DayError { get; init; }
}

public sealed class DashboardHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DashboardHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The sales section for a typed range. A range that fails the page's own checks is never sent;
    /// Reporting's own refusal of the dates is shown the same way.
    /// </summary>
    public async Task<SalesSection> LoadSalesAsync(string? from, string? to, string correlationId, CancellationToken cancellationToken)
    {
        ReportRangeCheck check = ReportDisplay.CheckRange(from, to);
        if (check.Range is null)
        {
            return new SalesSection { Check = check };
        }

        string address = QueryHelpers.AddQueryString("reports/operator/summary", "from", ReportDisplay.WireDate(check.Range.From));
        address = QueryHelpers.AddQueryString(address, "to", ReportDisplay.WireDate(check.Range.To));
        ApiResult<OperatorSummary> api = await GetAsync<OperatorSummary>(address, cancellationToken);
        if (api.IsSuccess)
        {
            return new SalesSection { Summary = api.Value, Check = check };
        }

        if (api.Errors != null)
        {
            return new SalesSection { Check = new ReportRangeCheck { RangeError = Messages.ValidationDateRange } };
        }

        return new SalesSection { Alert = ToAlert(api, correlationId), Check = check };
    }

    /// <summary>The trips section for a typed day. A day that is not a date is never sent.</summary>
    public async Task<TripsSection> LoadTripsAsync(string? day, string correlationId, CancellationToken cancellationToken)
    {
        DateOnly? date = ReportDisplay.ParseDay(day);
        if (date is null)
        {
            return new TripsSection { DayError = Messages.ValidationFormat("date") };
        }

        string address = QueryHelpers.AddQueryString("reports/operator/trips", "date", ReportDisplay.WireDate(date.Value));
        ApiResult<OperatorTripList> api = await GetAsync<OperatorTripList>(address, cancellationToken);
        if (api.IsSuccess)
        {
            return new TripsSection { Trips = api.Value };
        }

        if (api.Errors != null)
        {
            return new TripsSection { DayError = Messages.ValidationFormat("date") };
        }

        return new TripsSection { Alert = ToAlert(api, correlationId) };
    }

    /// <summary>"Mumbai → Pune".</summary>
    public static string RouteName(TripOccupancy trip)
    {
        return $"{trip.Source} → {trip.Destination}";
    }

    /// <summary>"30 of 40 seats sold (75%)".</summary>
    public static string SeatsSoldText(TripOccupancy trip)
    {
        int percent = Percent(trip.SeatsSold, trip.SeatsTotal);
        return $"{trip.SeatsSold} of {trip.SeatsTotal} seats sold ({percent}%)";
    }

    /// <summary>Seats sold over seats in total as a whole percentage, rounded to the nearest; a trip with no seats is 0%.</summary>
    public static int Percent(int seatsSold, int seatsTotal)
    {
        if (seatsTotal <= 0)
        {
            return 0;
        }

        double share = seatsSold * 100.0 / seatsTotal;
        return (int)Math.Round(share, MidpointRounding.AwayFromZero);
    }

    /// <summary>"2 trips." and "1 trip.".</summary>
    public static string TripsCountText(int count)
    {
        if (count == 1)
        {
            return "1 trip.";
        }

        return $"{count} trips.";
    }

    /// <summary>"No trips leave on 6 Oct 2026."</summary>
    public static string NoTripsText(DateOnly day)
    {
        return $"No trips leave on {IndiaTime.FormatDay(day)}.";
    }

    /// <summary>The trip's seats page, passing the route label on the way the trip list does.</summary>
    public static string SeatsAddress(TripOccupancy trip)
    {
        string address = $"/operator/trips/{Uri.EscapeDataString(trip.TripId)}/seats";
        return SeatDisplay.WithRoute(address, RouteName(trip));
    }

    /// <summary>The trip's passenger list, passing the route label on.</summary>
    public static string PassengersAddress(TripOccupancy trip)
    {
        string address = $"/operator/trips/{Uri.EscapeDataString(trip.TripId)}/passengers";
        return SeatDisplay.WithRoute(address, RouteName(trip));
    }

    /// <summary>"Trips on 5 Oct 2026".</summary>
    public static string TripsHeading(DateOnly day)
    {
        return "Trips on " + IndiaTime.FormatDay(day);
    }

    private async Task<ApiResult<T>> GetAsync<T>(string address, CancellationToken cancellationToken)
    {
        HttpClient reporting = _httpClientFactory.CreateClient(HttpClients.Reporting);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await reporting.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<T>(cancellationToken);
    }

    // Reporting answers only an operator; any refusal without words of its own is the shared one.
    private static PageAlert ToAlert<T>(ApiResult<T> api, string correlationId)
    {
        if (api.Problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(correlationId));
        }

        return SharedProblems.ToAlert(api.Problem, correlationId);
    }
}
