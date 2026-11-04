namespace RouteGo.App.Features.Operator.Dashboard;

public sealed class DashboardModel : PageModel
{
    private const string DashboardPage = "/Operator/Dashboard/Dashboard";
    private const string FromKey = "from";
    private const string ToKey = "to";
    private const string DayKey = "day";
    private const string SalesPartial = "_Sales";
    private const string TripsPartial = "_Trips";

    private readonly DashboardHandler _handler;
    private readonly IClock _clock;

    public DashboardModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new DashboardHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The From box as typed, or the day six days before today when the address names none.</summary>
    public string FromText { get; private set; } = string.Empty;

    public string ToText { get; private set; } = string.Empty;

    public string DayText { get; private set; } = string.Empty;

    public SalesSection Sales { get; private set; } = new SalesSection();

    public TripsSection Trips { get; private set; } = new TripsSection();

    /// <summary>Opens on the last 7 days and today's trips; the address always carries all three days, so a link or a reload shows the same page.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!HasEveryDay())
        {
            return RedirectToPage(DashboardPage, ResolvedValues());
        }

        ReadDays();
        string correlationId = CorrelationId.Get(HttpContext);
        Task<SalesSection> sales = _handler.LoadSalesAsync(FromText, ToText, correlationId, cancellationToken);
        Task<TripsSection> trips = _handler.LoadTripsAsync(DayText, correlationId, cancellationToken);
        await Task.WhenAll(sales, trips);
        Sales = await sales;
        Trips = await trips;
        return Page();
    }

    /// <summary>"Show figures" for a script-driven page: the sales section alone, from Reporting; the trips section is not asked again.</summary>
    public async Task<IActionResult> OnGetSalesAsync(CancellationToken cancellationToken)
    {
        ReadDays();
        Sales = await _handler.LoadSalesAsync(FromText, ToText, CorrelationId.Get(HttpContext), cancellationToken);
        return Partial(SalesPartial, this);
    }

    /// <summary>"Show trips" for a script-driven page: the trips section alone; the sales section is not asked again.</summary>
    public async Task<IActionResult> OnGetTripsAsync(CancellationToken cancellationToken)
    {
        ReadDays();
        Trips = await _handler.LoadTripsAsync(DayText, CorrelationId.Get(HttpContext), cancellationToken);
        return Partial(TripsPartial, this);
    }

    /// <summary>The page's own address with all three days: what a script writes to the address bar after a section reloads.</summary>
    public string PageAddress()
    {
        return Url.Page(DashboardPage, ResolvedValues())!;
    }

    /// <summary>Where a section's script fetches it from: the named step, keeping all three days.</summary>
    public string SectionAddress(string handler)
    {
        return Url.Page(DashboardPage, handler, ResolvedValues())!;
    }

    /// <summary>The day of the trips section as a date; null when the box holds none.</summary>
    public DateOnly? Day()
    {
        return ReportDisplay.ParseDay(DayText);
    }

    private bool HasEveryDay()
    {
        IQueryCollection query = Request.Query;
        return query.ContainsKey(FromKey) && query.ContainsKey(ToKey) && query.ContainsKey(DayKey);
    }

    // A day missing from the address takes its default; a box left empty stays empty and is told so.
    private void ReadDays()
    {
        DateOnly today = IndiaTime.Today(_clock.UtcNow);
        FromText = DayOrDefault(FromKey, ReportDisplay.WireDate(today.AddDays(1 - ReportDisplay.DefaultRangeDays)));
        ToText = DayOrDefault(ToKey, ReportDisplay.WireDate(today));
        DayText = DayOrDefault(DayKey, ReportDisplay.WireDate(today));
    }

    private string DayOrDefault(string key, string whenMissing)
    {
        if (!Request.Query.ContainsKey(key))
        {
            return whenMissing;
        }

        string? typed = Request.Query[key];
        return (typed ?? string.Empty).Trim();
    }

    private RouteValueDictionary ResolvedValues()
    {
        ReadDays();
        return new RouteValueDictionary { [FromKey] = FromText, [ToKey] = ToText, [DayKey] = DayText };
    }
}
