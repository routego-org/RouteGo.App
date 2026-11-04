using System.Globalization;

namespace RouteGo.App.Features.Search.BusSearch;

/// <summary>
/// How a search result is shown: times in IST, the journey time, the stops on the way, the price
/// and the seats left, its links, and what the page says when a search comes back empty.
/// </summary>
public static class ListingDisplay
{
    private const int MinutesPerHour = 60;

    /// <summary>"10:00 PM IST".</summary>
    public static string Departs(TripListing trip)
    {
        return IndiaTime.FormatTime(trip.DepartureAt);
    }

    /// <summary>"1:00 AM IST, 26 Oct" when the bus arrives on a later day than it leaves; the time alone otherwise.</summary>
    public static string Arrives(TripListing trip)
    {
        string time = IndiaTime.FormatTime(trip.ArrivalAt);
        if (IndiaTime.Today(trip.ArrivalAt) == IndiaTime.Today(trip.DepartureAt))
        {
            return time;
        }

        return time + ", " + IndiaTime.FormatDayMonth(trip.ArrivalAt);
    }

    /// <summary>"3 h 0 min", or "45 min" for a journey under an hour.</summary>
    public static string JourneyTime(int minutes)
    {
        int hours = minutes / MinutesPerHour;
        int rest = minutes % MinutesPerHour;
        string restText = rest.ToString(CultureInfo.InvariantCulture) + " min";
        if (hours == 0)
        {
            return restText;
        }

        return hours.ToString(CultureInfo.InvariantCulture) + " h " + restText;
    }

    /// <summary>"Via Lonavala 11:30 PM IST".</summary>
    public static string Via(ListingStop stop)
    {
        return "Via " + stop.CityName + " " + IndiaTime.FormatTime(stop.ArrivalAt);
    }

    /// <summary>"Rating 4.2 out of 5".</summary>
    public static string Rating(decimal rating)
    {
        return "Rating " + rating.ToString("0.0", CultureInfo.InvariantCulture) + " out of 5";
    }

    /// <summary>"from ₹900".</summary>
    public static string Price(TripListing trip)
    {
        return "from " + TripDisplay.Rupees(trip.LowestFreeFare);
    }

    /// <summary>"36 seats left", "1 seat left", or "Sold out".</summary>
    public static string SeatsLeft(TripListing trip)
    {
        if (trip.IsSoldOut)
        {
            return Messages.StateSoldOut;
        }

        if (trip.SeatsLeft == 1)
        {
            return "1 seat left";
        }

        return trip.SeatsLeft.ToString(CultureInfo.InvariantCulture) + " seats left";
    }

    /// <summary>The bus's public page.</summary>
    public static string BusAddress(TripListing trip)
    {
        return "/buses/" + trip.BusId;
    }

    /// <summary>The trip's public seat map, with the route as its heading ("Mumbai → Pune").</summary>
    public static string SeatsAddress(TripListing trip)
    {
        return SeatDisplay.WithRoute($"/trips/{trip.TripId}/seats", trip.FromCity.Name + " → " + trip.ToCity.Name);
    }

    /// <summary>The search's travel date as shown: "25 Oct 2026". Only called once the date passed the checks.</summary>
    public static string TravelDate(SearchFormInput search)
    {
        DateOnly date = SearchFormChecks.ReadDate(search.Date)!.Value;
        return IndiaTime.FormatDay(date);
    }

    /// <summary>
    /// An address may name a city by identifier only; the first result then gives its name to the
    /// title and the form. With no result the box stays empty.
    /// </summary>
    public static void FillMissingNames(SearchFormInput search, TripListingPage results)
    {
        if (results.Items.Count == 0)
        {
            return;
        }

        TripListing first = results.Items[0];
        if (string.IsNullOrEmpty(search.FromCityName))
        {
            search.FromCityName = first.FromCity.Name;
            search.FromCity = first.FromCity.Name;
        }

        if (string.IsNullOrEmpty(search.ToCityName))
        {
            search.ToCityName = first.ToCity.Name;
            search.ToCity = first.ToCity.Name;
        }
    }

    /// <summary>What the page says when the page of results is empty, or null when there are results.</summary>
    public static PageAlert? EmptyState(SearchFormInput search, TripListingPage results)
    {
        if (results.Items.Count > 0)
        {
            return null;
        }

        if (results.TotalCount > 0)
        {
            return PageAlert.Info("There are no buses on this page.").WithLink("Go to the first page", SearchForm.ResultsAddress(search, 1));
        }

        if (SearchForm.HasFilters(search))
        {
            return PageAlert.Info(Messages.EmptyFiltered(BusSearchModel.Items)).WithLink("Clear filters", SearchForm.ClearFiltersAddress(search));
        }

        string date = TravelDate(search);
        bool isNamed = !string.IsNullOrEmpty(search.FromCityName) && !string.IsNullOrEmpty(search.ToCityName);
        if (!isNamed)
        {
            return PageAlert.Info($"No buses for this route on {date}. Try another date.");
        }

        return PageAlert.Info($"No buses from {search.FromCityName} to {search.ToCityName} on {date}. Try another date.");
    }
}
