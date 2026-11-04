namespace RouteGo.App.Features.Search.BusSearch;

/// <summary>A city at one end of a trip, as Search names it.</summary>
public sealed record ListingCity(Guid CityId, string Name);

/// <summary>A stop on the way, shown for information only.</summary>
public sealed record ListingStop(Guid CityId, string CityName, DateTimeOffset ArrivalAt);

/// <summary>One bookable or sold-out trip of a search answer.</summary>
public sealed record TripListing
{
    public required Guid TripId { get; init; }

    public required string OperatorName { get; init; }

    /// <summary>The bus company's rating, 0.0 to 5.0.</summary>
    public required decimal OperatorRating { get; init; }

    public required Guid BusId { get; init; }

    public required string BusName { get; init; }

    /// <summary>seater, sleeper or semiSleeper.</summary>
    public required string BusType { get; init; }

    public required bool IsAc { get; init; }

    public required IReadOnlyList<string> Amenities { get; init; }

    public required ListingCity FromCity { get; init; }

    public required ListingCity ToCity { get; init; }

    public required DateOnly ServiceDate { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    public required DateTimeOffset ArrivalAt { get; init; }

    public required int DurationMinutes { get; init; }

    public required IReadOnlyList<ListingStop> Stops { get; init; }

    /// <summary>The lowest fare among free seats in whole rupees; for a sold-out trip, the lowest of any seat.</summary>
    public required int LowestFreeFare { get; init; }

    public required int SeatsLeft { get; init; }

    public required bool IsSoldOut { get; init; }
}

/// <summary>One page of trips, bookable first and sold out after, in Search's order.</summary>
public sealed record TripListingPage
{
    public required IReadOnlyList<TripListing> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class BusSearchHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public BusSearchHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// One page of Search's trip search. Sent without the person's access token: the page is open to
    /// everyone and Search's trip search is public.
    /// </summary>
    public async Task<ApiResult<TripListingPage>> SearchTripsAsync(string tripsQuery, CancellationToken cancellationToken)
    {
        HttpClient search = _httpClientFactory.CreateClient(HttpClients.Search);
        HttpResponseMessage response = await search.GetAsync(tripsQuery, cancellationToken);
        return await response.ReadApiResultAsync<TripListingPage>(cancellationToken);
    }
}

public sealed class BusSearchModel : PageModel
{
    /// <summary>The results' plural name in the page's sentences.</summary>
    public const string Items = "buses";

    private const string RefusedWithoutTitle = "The request could not be completed.";

    private readonly BusSearchHandler _handler;
    private readonly SearchFormHandler _formHandler;
    private readonly IClock _clock;
    private CitySuggestions _fromSuggestions = CitySuggestions.None;
    private CitySuggestions _toSuggestions = CitySuggestions.None;

    public BusSearchModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new BusSearchHandler(httpClientFactory);
        _formHandler = new SearchFormHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The search as the form shows it: read from the address, or as posted.</summary>
    [BindProperty]
    public SearchFormInput Search { get; set; } = new SearchFormInput();

    /// <summary>The loaded page of trips; null when Search was not asked or refused.</summary>
    public TripListingPage? Results { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>What the page says in place of results that came back empty, with its next step.</summary>
    public PageAlert? Empty { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        DateOnly today = IndiaTime.Today(_clock.UtcNow);
        Search = SearchForm.FromAddress(Request.Query, today);
        if (!SearchForm.HasCities(Request.Query))
        {
            return;
        }

        Dictionary<string, string> errors = SearchFormChecks.CheckAddress(Search, today);
        if (errors.Count > 0)
        {
            SearchFormChecks.AddErrors(ModelState, errors);
            return;
        }

        string tripsQuery = SearchForm.TripsQuery(Search, SearchForm.PageValue(Request.Query));
        ApiResult<TripListingPage> api = await _handler.SearchTripsAsync(tripsQuery, cancellationToken);
        if (!api.IsSuccess)
        {
            ShowRefusal(api);
            return;
        }

        Results = api.Value!;
        ListingDisplay.FillMissingNames(Search, Results);
        Empty = ListingDisplay.EmptyState(Search, Results);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        DateOnly today = IndiaTime.Today(_clock.UtcNow);
        SearchFormOutcome outcome = await _formHandler.SubmitAsync(Search, today, cancellationToken);
        if (outcome.ResultsRoute != null)
        {
            return RedirectToPage(SearchForm.ResultsPage, outcome.ResultsRoute);
        }

        SearchFormChecks.AddErrors(ModelState, outcome.Errors);
        _fromSuggestions = outcome.FromSuggestions;
        _toSuggestions = outcome.ToSuggestions;
        return Page();
    }

    /// <summary>City suggestions for the page script, on this page and on the home page; the words are made here.</summary>
    public async Task<IActionResult> OnGetCitiesAsync(string? prefix, string? cityRole, string? fromCityId, CancellationToken cancellationToken)
    {
        CitySuggestions suggestions = await _formHandler.SuggestAsync(prefix, cityRole, fromCityId, cancellationToken);
        return new JsonResult(suggestions);
    }

    public SearchFormView Form()
    {
        return new SearchFormView
        {
            Input = Search,
            PostAddress = $"{Request.PathBase}{Request.Path}{Request.QueryString}",
            Today = IndiaTime.Today(_clock.UtcNow),
            ShowsFilters = true,
            FromSuggestions = _fromSuggestions,
            ToSuggestions = _toSuggestions,
        };
    }

    /// <summary>"Buses from Mumbai to Pune on 25 Oct 2026" once results are loaded and both cities are named; otherwise "Find a bus".</summary>
    public string Heading()
    {
        bool isNamed = !string.IsNullOrEmpty(Search.FromCityName) && !string.IsNullOrEmpty(Search.ToCityName);
        if (Results is null || !isNamed)
        {
            return SearchForm.FormTitle;
        }

        return $"Buses from {Search.FromCityName} to {Search.ToCityName} on {ListingDisplay.TravelDate(Search)}";
    }

    /// <summary>"Showing 21–40 of 45 buses."</summary>
    public string ShowingText()
    {
        int from = ((Results!.Page - 1) * Results.PageSize) + 1;
        int to = from + Results.Items.Count - 1;
        return $"Showing {from}–{to} of {Results.TotalCount} {Items}.";
    }

    public int PageCount()
    {
        return (Results!.TotalCount + Results.PageSize - 1) / Results.PageSize;
    }

    public string PageAddress(int page)
    {
        return SearchForm.ResultsAddress(Search, page);
    }

    // Shared rule 3: a refused field the form shows gets its message there; any other is told at
    // the top with the problem's title.
    private void ShowRefusal(ApiResult<TripListingPage> api)
    {
        if (api.Errors is null)
        {
            Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
            return;
        }

        SearchFormChecks.AddErrors(ModelState, SearchFormChecks.FieldErrors(api.Errors));
        if (!SearchFormChecks.NamesOtherFields(api.Errors))
        {
            return;
        }

        string title = RefusedWithoutTitle;
        if (!string.IsNullOrEmpty(api.ErrorsTitle))
        {
            title = api.ErrorsTitle;
        }

        Alert = PageAlert.Danger(title);
    }
}
