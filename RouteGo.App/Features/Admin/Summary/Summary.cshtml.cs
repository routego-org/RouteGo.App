namespace RouteGo.App.Features.Admin.Summary;

public sealed class SummaryModel : PageModel
{
    private const string SummaryPage = "/Admin/Summary/Summary";
    private const string FromKey = "from";
    private const string ToKey = "to";

    private readonly SummaryHandler _handler;
    private readonly IClock _clock;

    public SummaryModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new SummaryHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The From box as typed, or the day six days before today when the address names none.</summary>
    public string FromText { get; private set; } = string.Empty;

    public string ToText { get; private set; } = string.Empty;

    public SummaryView View { get; private set; } = new SummaryView();

    /// <summary>Opens on the last 7 days; the address always carries both days, so a link or a reload shows the same page.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ReadDays();
        if (!HasBothDays())
        {
            return RedirectToPage(SummaryPage, new RouteValueDictionary { [FromKey] = FromText, [ToKey] = ToText });
        }

        View = await _handler.LoadAsync(FromText, ToText, CorrelationId.Get(HttpContext), cancellationToken);
        return Page();
    }

    private bool HasBothDays()
    {
        IQueryCollection query = Request.Query;
        return query.ContainsKey(FromKey) && query.ContainsKey(ToKey);
    }

    // A day missing from the address takes its default; a box left empty stays empty and is told so.
    private void ReadDays()
    {
        DateOnly today = IndiaTime.Today(_clock.UtcNow);
        FromText = DayOrDefault(FromKey, ReportDisplay.WireDate(today.AddDays(1 - ReportDisplay.DefaultRangeDays)));
        ToText = DayOrDefault(ToKey, ReportDisplay.WireDate(today));
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
}
