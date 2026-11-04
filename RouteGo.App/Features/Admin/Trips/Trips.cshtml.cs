namespace RouteGo.App.Features.Admin.Trips;

public sealed class TripsModel : PageModel
{
    public const string Items = "trips";

    private const string TripsPageName = "/Admin/Trips/Trips";
    private const string FromField = "from";
    private const string ToField = "to";

    private readonly TripSearchHandler _handler;
    private readonly IClock _clock;

    public TripsModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new TripSearchHandler(httpClientFactory);
        _clock = clock;
    }

    [BindProperty(SupportsGet = true)]
    public TripSearchQuery Query { get; set; } = new TripSearchQuery();

    /// <summary>The travel dates searched; null when the typed range is not allowed.</summary>
    public TripDates? Dates { get; private set; }

    /// <summary>The loaded list; null when it could not be loaded.</summary>
    public FoundTripsPage? Trips { get; private set; }

    /// <summary>The cities for the From city and To city filters; null when Fleet would not give them, and then those filters are not offered.</summary>
    public IReadOnlyList<SearchCity>? Cities { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Task<ApiResult<SearchCityList>> cities = _handler.ListCitiesAsync(cancellationToken);
        Dates = TripDisplay.ListDates(Query.From, Query.To, Today());
        if (Dates != null)
        {
            await ShowTripsAsync(cancellationToken);
        }

        ApiResult<SearchCityList> cityList = await cities;
        Cities = cityList.Value?.Items;
    }

    /// <summary>The list address with the current filters and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(TripsPageName, ListValues(page, Query.HasCompany()))!;
    }

    /// <summary>The list address without the company, keeping the other filters.</summary>
    public string AllCompaniesAddress()
    {
        return Url.Page(TripsPageName, ListValues(1, false))!;
    }

    /// <summary>"Company: {company name}", falling back to the identifier when no name came with it.</summary>
    public string CompanyText()
    {
        string company = Query.OperatorId!;
        if (!string.IsNullOrEmpty(Query.OperatorName))
        {
            company = Query.OperatorName;
        }

        return "Company: " + company;
    }

    private DateOnly Today()
    {
        return IndiaTime.Today(_clock.UtcNow);
    }

    // A refused filter in the address (not the dates) shows the default search instead, with why;
    // refused dates show the range message with no list.
    private async Task ShowTripsAsync(CancellationToken cancellationToken)
    {
        ApiResult<FoundTripsPage> api = await _handler.SearchTripsAsync(Query, Dates!, cancellationToken);
        if (api.Errors != null && !NamesDates(api.Errors))
        {
            Alert = PageAlert.Danger(api.ErrorsTitle!);
            Query = new TripSearchQuery();
            Dates = TripDisplay.ListDates(null, null, Today());
            api = await _handler.SearchTripsAsync(Query, Dates!, cancellationToken);
        }

        if (api.IsSuccess)
        {
            Trips = api.Value;
            return;
        }

        if (api.Errors != null)
        {
            Dates = null;
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }

    private RouteValueDictionary ListValues(int page, bool isCompanyKept)
    {
        var values = new RouteValueDictionary();
        if (isCompanyKept)
        {
            values["operatorId"] = Query.OperatorId;
            values["operatorName"] = Query.OperatorName;
        }

        values["sourceCityId"] = Query.SourceCityId;
        values["destinationCityId"] = Query.DestinationCityId;
        values["from"] = Query.From;
        values["to"] = Query.To;
        values["status"] = Query.Status;
        values["pageNumber"] = page;
        return values;
    }

    private static bool NamesDates(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (string field in errors.Keys)
        {
            bool isDate = string.Equals(field, FromField, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field, ToField, StringComparison.OrdinalIgnoreCase);
            if (isDate)
            {
                return true;
            }
        }

        return false;
    }
}
