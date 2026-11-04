using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The search form's work with Search, shared by the home page and the results page: city
/// suggestions for the page script, and "Search" — the checks, then either the results address or,
/// for a city typed but not picked, the cities to pick from without the page script. Search is
/// public, so no call carries the person's access token.
/// </summary>
public sealed class SearchFormHandler
{
    /// <summary>Search takes at most 50 letters; a longer start of a name matches no city anyway.</summary>
    private const int MaxPrefixLetters = 50;

    private static readonly CitySuggestions _noMatch =
        new CitySuggestions(Array.Empty<SuggestedCity>(), SearchForm.NoMatchingCities);

    private static readonly CitySuggestions _unavailable =
        new CitySuggestions(Array.Empty<SuggestedCity>(), SearchForm.SuggestionsUnavailable);

    private readonly IHttpClientFactory _httpClientFactory;

    public SearchFormHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The cities whose names start with what was typed. To-city suggestions follow the picked From
    /// city when there is one. Nothing is asked until a letter is typed; any problem reaching Search
    /// becomes the suggestions-unavailable note, so the rest of the page keeps working.
    /// </summary>
    public async Task<CitySuggestions> SuggestAsync(string? prefix, string? cityRole, string? fromCityId, CancellationToken cancellationToken)
    {
        string typed = Typed(prefix);
        if (typed.Length == 0)
        {
            return CitySuggestions.None;
        }

        if (typed.Length > MaxPrefixLetters)
        {
            return _noMatch;
        }

        string address = CitiesQuery(typed, cityRole, fromCityId);
        try
        {
            HttpClient search = _httpClientFactory.CreateClient(HttpClients.Search);
            HttpResponseMessage response = await search.GetAsync(address, cancellationToken);
            ApiResult<CityPage> api = await response.ReadApiResultAsync<CityPage>(cancellationToken);
            return ToSuggestions(api);
        }
        catch (HttpRequestException)
        {
            // A server error without a problem body: suggestions are a help, never a page failure.
            return _unavailable;
        }
    }

    /// <summary>"Search": the results address when every check passes, otherwise the form's messages and the cities to pick from.</summary>
    public async Task<SearchFormOutcome> SubmitAsync(SearchFormInput input, DateOnly today, CancellationToken cancellationToken)
    {
        SearchFormChecks.ApplyPicks(input);
        Dictionary<string, string> errors = SearchFormChecks.CheckForm(input, today);
        if (errors.Count == 0)
        {
            return ToResults(input, errors);
        }

        string? pickedFromCityId = null;
        if (!errors.ContainsKey(nameof(SearchFormInput.FromCity)))
        {
            pickedFromCityId = input.FromCityId;
        }

        Task<CitySuggestions> fromSuggestions = SuggestWhenNotPickedAsync(errors, nameof(SearchFormInput.FromCity), input.FromCity, SearchForm.FromRole, null, cancellationToken);
        Task<CitySuggestions> toSuggestions = SuggestWhenNotPickedAsync(errors, nameof(SearchFormInput.ToCity), input.ToCity, SearchForm.ToRole, pickedFromCityId, cancellationToken);
        await Task.WhenAll(fromSuggestions, toSuggestions);
        return new SearchFormOutcome
        {
            Errors = errors,
            FromSuggestions = await fromSuggestions,
            ToSuggestions = await toSuggestions,
        };
    }

    private Task<CitySuggestions> SuggestWhenNotPickedAsync(
        Dictionary<string, string> errors, string field, string? text, string cityRole, string? fromCityId, CancellationToken cancellationToken)
    {
        bool isTypedNotPicked = errors.TryGetValue(field, out string? message) && message == SearchForm.ChooseCity;
        if (!isTypedNotPicked)
        {
            return Task.FromResult(CitySuggestions.None);
        }

        return SuggestAsync(text, cityRole, fromCityId, cancellationToken);
    }

    private static string Typed(string? prefix)
    {
        if (prefix is null)
        {
            return string.Empty;
        }

        return prefix.Trim();
    }

    private static SearchFormOutcome ToResults(SearchFormInput input, Dictionary<string, string> errors)
    {
        return new SearchFormOutcome
        {
            ResultsRoute = new RouteValueDictionary(SearchForm.FirstPageValues(input)),
            Errors = errors,
            FromSuggestions = CitySuggestions.None,
            ToSuggestions = CitySuggestions.None,
        };
    }

    private static string CitiesQuery(string prefix, string? cityRole, string? fromCityId)
    {
        string role = SearchForm.FromRole;
        if (cityRole == SearchForm.ToRole)
        {
            role = SearchForm.ToRole;
        }

        var values = new List<KeyValuePair<string, string?>>
        {
            new KeyValuePair<string, string?>("prefix", prefix),
            new KeyValuePair<string, string?>("cityRole", role),
        };
        if (role == SearchForm.ToRole && Guid.TryParse(fromCityId, out Guid fromCity))
        {
            values.Add(new KeyValuePair<string, string?>("fromCityId", fromCity.ToString()));
        }

        return QueryHelpers.AddQueryString("search/cities", values);
    }

    private static CitySuggestions ToSuggestions(ApiResult<CityPage> api)
    {
        if (!api.IsSuccess)
        {
            return _unavailable;
        }

        if (api.Value!.Items.Count == 0)
        {
            return _noMatch;
        }

        return new CitySuggestions(api.Value.Items, null);
    }

    /// <summary>Search's suggestion answer: at most ten cities, by name.</summary>
    private sealed record CityPage(IReadOnlyList<SuggestedCity> Items);
}
