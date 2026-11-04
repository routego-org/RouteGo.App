using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The bus search form's words, choices and addresses, shared by the home page and the results
/// page. The results address holds the whole search in Search's own parameter names, so a reload,
/// the Back button or a shared link shows the same results; the city names in it are only labels.
/// </summary>
public static class SearchForm
{
    /// <summary>The name both pages bind the form under, so every field posts as "Search.{field}".</summary>
    public const string Prefix = "Search";

    /// <summary>The results page. Never under /search: Search's own operations may be published there.</summary>
    public const string ResultsPath = "/bus-search";

    /// <summary>The results page's page name, for a redirect after "Search".</summary>
    public const string ResultsPage = "/Search/BusSearch/BusSearch";

    /// <summary>Where the page script asks for city suggestions.</summary>
    public const string CitiesAddress = ResultsPath + "?handler=Cities";

    public const string FromRole = "from";
    public const string ToRole = "to";

    public const string FormTitle = "Find a bus";
    public const string FromLabel = "From city";
    public const string ToLabel = "To city";
    public const string DateLabel = "Travel date";
    public const string BusTypeLabel = "Bus type";
    public const string AcLabel = "AC";
    public const string DepartureLabel = "Departure time";
    public const string PriceFromLabel = "Price from";
    public const string PriceUpToLabel = "Price up to";
    public const string RatingLabel = "Rating";
    public const string SortLabel = "Sort by";

    public const string ChooseCity = "Choose a city from the list.";
    public const string DifferentCities = "Choose a To city different from the From city.";
    public const string TodayOrLater = "Choose today or a later date.";
    public const string PriceOrder = "Price from must not be more than Price up to.";
    public const string NoMatchingCities = "No matching cities.";
    public const string SuggestionsUnavailable = "We can't suggest cities right now. Please try again in a moment.";

    /// <summary>How the address, the date box and Search write a date.</summary>
    public const string DateFormat = "yyyy-MM-dd";
    private const string BusTypeSeparator = ",";
    private const string FirstPage = "1";

    public static readonly IReadOnlyList<SearchChoice> BusTypeChoices = new[]
    {
        new SearchChoice("seater", Messages.BusTypeLabel("seater")),
        new SearchChoice("sleeper", Messages.BusTypeLabel("sleeper")),
        new SearchChoice("semiSleeper", Messages.BusTypeLabel("semiSleeper")),
    };

    public static readonly IReadOnlyList<SearchChoice> AcChoices = new[]
    {
        new SearchChoice("", "Any"),
        new SearchChoice("true", "AC"),
        new SearchChoice("false", "Non-AC"),
    };

    public static readonly IReadOnlyList<SearchChoice> RatingChoices = new[]
    {
        new SearchChoice("", "Any"),
        new SearchChoice("3.0", "3.0 and above"),
        new SearchChoice("3.5", "3.5 and above"),
        new SearchChoice("4.0", "4.0 and above"),
        new SearchChoice("4.5", "4.5 and above"),
    };

    /// <summary>Departure time first: it is Search's default order, so the address leaves it out.</summary>
    public static readonly IReadOnlyList<SearchChoice> SortChoices = new[]
    {
        new SearchChoice("", "Departure time"),
        new SearchChoice("lowestFreeFare", "Lowest price"),
        new SearchChoice("operatorRating", "Highest rating"),
    };

    // Each departure slot is one address value that the portal turns into Search's "depart after"
    // and "depart before" (IST times of day, both included).
    private static readonly DepartureSlot[] _departureSlots = new[]
    {
        new DepartureSlot { Value = "earlyMorning", Label = "Early morning (before 6 AM)", After = "00:00", Before = "05:59" },
        new DepartureSlot { Value = "morning", Label = "Morning (6 AM – 12 PM)", After = "06:00", Before = "11:59" },
        new DepartureSlot { Value = "afternoon", Label = "Afternoon (12 PM – 6 PM)", After = "12:00", Before = "17:59" },
        new DepartureSlot { Value = "night", Label = "Night (after 6 PM)", After = "18:00", Before = "23:59" },
    };

    /// <summary>"Any time", then each departure slot.</summary>
    public static IReadOnlyList<SearchChoice> DepartureChoices()
    {
        var choices = new List<SearchChoice> { new SearchChoice("", "Any time") };
        foreach (DepartureSlot slot in _departureSlots)
        {
            choices.Add(new SearchChoice(slot.Value, slot.Label));
        }

        return choices;
    }

    /// <summary>Whether the address names a search at all; without cities the page shows only the form.</summary>
    public static bool HasCities(IQueryCollection query)
    {
        return Value(query, "fromCityId") != null || Value(query, "toCityId") != null;
    }

    /// <summary>
    /// The search an address holds, as the form shows it. The travel date is today when the address
    /// has none; an unknown departure slot counts as any time. Everything else is kept as written,
    /// so Search itself refuses a value it does not take.
    /// </summary>
    public static SearchFormInput FromAddress(IQueryCollection query, DateOnly today)
    {
        string? fromName = Value(query, "fromCityName");
        string? toName = Value(query, "toCityName");
        string date = WireDate(today);
        string? addressDate = Value(query, "date");
        if (addressDate != null)
        {
            date = addressDate;
        }

        return new SearchFormInput
        {
            FromCity = fromName,
            FromCityId = Value(query, "fromCityId"),
            FromCityName = fromName,
            ToCity = toName,
            ToCityId = Value(query, "toCityId"),
            ToCityName = toName,
            Date = date,
            BusTypes = SplitBusTypes(Value(query, "busTypes")),
            IsAc = Value(query, "isAc"),
            Departure = KnownDeparture(Value(query, "departure")),
            MinFare = Value(query, "minFare"),
            MaxFare = Value(query, "maxFare"),
            MinRating = Value(query, "minRating"),
            Sort = Value(query, "sort"),
        };
    }

    /// <summary>The page number as the address writes it, passed on to Search unread.</summary>
    public static string? PageValue(IQueryCollection query)
    {
        return Value(query, "page");
    }

    /// <summary>The results address's values on page 1: cities, date, the chosen filters and sort.</summary>
    public static List<KeyValuePair<string, string?>> FirstPageValues(SearchFormInput input)
    {
        List<KeyValuePair<string, string?>> values = CitiesAndDate(input);
        AddFilters(values, input);
        Add(values, "sort", input.Sort);
        return values;
    }

    /// <summary>The results address for a search on a page; the page is written only past the first.</summary>
    public static string ResultsAddress(SearchFormInput input, int page)
    {
        List<KeyValuePair<string, string?>> values = FirstPageValues(input);
        if (page > 1)
        {
            Add(values, "page", page.ToString(CultureInfo.InvariantCulture));
        }

        return QueryHelpers.AddQueryString(ResultsPath, values);
    }

    /// <summary>"Clear filters": the same cities, date and sort with every filter removed, on page 1.</summary>
    public static string ClearFiltersAddress(SearchFormInput input)
    {
        List<KeyValuePair<string, string?>> values = CitiesAndDate(input);
        Add(values, "sort", input.Sort);
        return QueryHelpers.AddQueryString(ResultsPath, values);
    }

    /// <summary>Whether any filter is chosen (the sort is not a filter).</summary>
    public static bool HasFilters(SearchFormInput input)
    {
        if (input.BusTypes.Count > 0)
        {
            return true;
        }

        string?[] filters = new[] { input.IsAc, input.Departure, input.MinFare, input.MaxFare, input.MinRating };
        return filters.Any(filter => !string.IsNullOrEmpty(filter));
    }

    /// <summary>Search's trip search for the form's search and the page as the address wrote it.</summary>
    public static string TripsQuery(SearchFormInput input, string? page)
    {
        var values = new List<KeyValuePair<string, string?>>();
        Add(values, "fromCityId", input.FromCityId);
        Add(values, "toCityId", input.ToCityId);
        Add(values, "date", input.Date);
        Add(values, "busTypes", string.Join(BusTypeSeparator, input.BusTypes));
        Add(values, "isAc", input.IsAc);
        Add(values, "minFare", input.MinFare);
        Add(values, "maxFare", input.MaxFare);
        DepartureSlot? slot = _departureSlots.FirstOrDefault(known => known.Value == input.Departure);
        if (slot != null)
        {
            Add(values, "departAfter", slot.After);
            Add(values, "departBefore", slot.Before);
        }

        Add(values, "minRating", input.MinRating);
        Add(values, "sort", input.Sort);
        Add(values, "page", page ?? FirstPage);
        return QueryHelpers.AddQueryString("search/trips", values);
    }

    /// <summary>A date as the address and Search write it: yyyy-MM-dd.</summary>
    public static string WireDate(DateOnly date)
    {
        return date.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    private static List<KeyValuePair<string, string?>> CitiesAndDate(SearchFormInput input)
    {
        var values = new List<KeyValuePair<string, string?>>();
        Add(values, "fromCityId", input.FromCityId);
        Add(values, "fromCityName", input.FromCityName);
        Add(values, "toCityId", input.ToCityId);
        Add(values, "toCityName", input.ToCityName);
        Add(values, "date", input.Date);
        return values;
    }

    private static void AddFilters(List<KeyValuePair<string, string?>> values, SearchFormInput input)
    {
        Add(values, "busTypes", string.Join(BusTypeSeparator, input.BusTypes));
        Add(values, "isAc", input.IsAc);
        Add(values, "minFare", input.MinFare);
        Add(values, "maxFare", input.MaxFare);
        Add(values, "departure", input.Departure);
        Add(values, "minRating", input.MinRating);
    }

    private static void Add(List<KeyValuePair<string, string?>> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        values.Add(new KeyValuePair<string, string?>(key, value.Trim()));
    }

    private static string? Value(IQueryCollection query, string key)
    {
        string? value = query[key].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static List<string> SplitBusTypes(string? busTypes)
    {
        if (busTypes is null)
        {
            return new List<string>();
        }

        return busTypes.Split(BusTypeSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string? KnownDeparture(string? departure)
    {
        bool isKnown = _departureSlots.Any(slot => slot.Value == departure);
        if (!isKnown)
        {
            return null;
        }

        return departure;
    }

    private sealed record DepartureSlot
    {
        public required string Value { get; init; }

        public required string Label { get; init; }

        public required string After { get; init; }

        public required string Before { get; init; }
    }
}
