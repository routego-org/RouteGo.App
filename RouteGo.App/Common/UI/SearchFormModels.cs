namespace RouteGo.App.Common.UI;

/// <summary>
/// What the bus search form posts: each city as typed and as picked, the travel date, and on the
/// results page the filters and the sort. Every value arrives as text, so a bad one is told to the
/// person rather than refused by model binding.
/// </summary>
public sealed class SearchFormInput
{
    /// <summary>The From box's text, as typed or as filled by a picked suggestion.</summary>
    public string? FromCity { get; set; }

    /// <summary>The picked From city's identifier; empty until a suggestion is picked.</summary>
    public string? FromCityId { get; set; }

    /// <summary>The picked From city's name: the pick counts only while the box still shows it.</summary>
    public string? FromCityName { get; set; }

    /// <summary>A From city chosen from the list the portal draws without the page script ("{id}|{name}").</summary>
    public string? FromPick { get; set; }

    public string? ToCity { get; set; }

    public string? ToCityId { get; set; }

    public string? ToCityName { get; set; }

    public string? ToPick { get; set; }

    /// <summary>The travel date as yyyy-MM-dd.</summary>
    public string? Date { get; set; }

    /// <summary>Search's bus types (seater, sleeper, semiSleeper); none means every type.</summary>
    public List<string> BusTypes { get; set; } = new List<string>();

    /// <summary>"true", "false", or empty for either.</summary>
    public string? IsAc { get; set; }

    /// <summary>The departure slot (earlyMorning, morning, afternoon, night), or empty for any time.</summary>
    public string? Departure { get; set; }

    public string? MinFare { get; set; }

    public string? MaxFare { get; set; }

    public string? MinRating { get; set; }

    /// <summary>Search's sort (lowestFreeFare, operatorRating), or empty for departure time.</summary>
    public string? Sort { get; set; }
}

/// <summary>One choice of a filter or sort list: the value in the address and the words shown.</summary>
public sealed record SearchChoice(string Value, string Label);

/// <summary>A city Search suggests, shown by its name only.</summary>
public sealed record SuggestedCity(Guid CityId, string Name);

/// <summary>
/// What a From or To box offers: Search's suggestions, or the note shown in their place ("No
/// matching cities." or the suggestions-unavailable message).
/// </summary>
public sealed record CitySuggestions(IReadOnlyList<SuggestedCity> Items, string? Note)
{
    /// <summary>Nothing asked, nothing to offer.</summary>
    public static readonly CitySuggestions None = new CitySuggestions(Array.Empty<SuggestedCity>(), null);
}

/// <summary>
/// The answer to "Search": either the results address to go to, or the form drawn again with each
/// field's message and, for a city that was typed but not picked, the cities to pick from.
/// </summary>
public sealed record SearchFormOutcome
{
    /// <summary>The results address's values (page 1) when every check passed; null when the form is drawn again.</summary>
    public RouteValueDictionary? ResultsRoute { get; init; }

    /// <summary>Each failed field's message, by the field's name in <see cref="SearchFormInput"/>.</summary>
    public required IReadOnlyDictionary<string, string> Errors { get; init; }

    public required CitySuggestions FromSuggestions { get; init; }

    public required CitySuggestions ToSuggestions { get; init; }
}

/// <summary>One city box of the search form, as Features/Shared/_SearchCityField.cshtml draws it.</summary>
public sealed record SearchCityField
{
    /// <summary>Search's city role: from or to.</summary>
    public required string Role { get; init; }

    public required string Label { get; init; }

    /// <summary>The text box's field name in <see cref="SearchFormInput"/> (FromCity / ToCity).</summary>
    public required string FieldName { get; init; }

    public required string? Text { get; init; }

    public required string? CityId { get; init; }

    public required string? CityName { get; init; }

    public required CitySuggestions Suggestions { get; init; }
}

/// <summary>Everything Features/Shared/_SearchForm.cshtml needs to draw the search form.</summary>
public sealed record SearchFormView
{
    public required SearchFormInput Input { get; init; }

    /// <summary>Where "Search" posts: the page's own address, so a failed check keeps the address.</summary>
    public required string PostAddress { get; init; }

    /// <summary>Today in India, the earliest travel date the date box offers.</summary>
    public required DateOnly Today { get; init; }

    /// <summary>Whether the form shows "Find a bus" as its own heading (the home page does).</summary>
    public bool ShowsTitle { get; init; }

    /// <summary>Whether the filters and the sort are part of the form (the results page).</summary>
    public bool ShowsFilters { get; init; }

    public CitySuggestions FromSuggestions { get; init; } = CitySuggestions.None;

    public CitySuggestions ToSuggestions { get; init; } = CitySuggestions.None;

    /// <summary>The From box.</summary>
    public SearchCityField FromField()
    {
        return new SearchCityField
        {
            Role = SearchForm.FromRole,
            Label = SearchForm.FromLabel,
            FieldName = nameof(SearchFormInput.FromCity),
            Text = Input.FromCity,
            CityId = Input.FromCityId,
            CityName = Input.FromCityName,
            Suggestions = FromSuggestions,
        };
    }

    /// <summary>The To box.</summary>
    public SearchCityField ToField()
    {
        return new SearchCityField
        {
            Role = SearchForm.ToRole,
            Label = SearchForm.ToLabel,
            FieldName = nameof(SearchFormInput.ToCity),
            Text = Input.ToCity,
            CityId = Input.ToCityId,
            CityName = Input.ToCityName,
            Suggestions = ToSuggestions,
        };
    }
}
