namespace RouteGo.App.Features.Admin.Settlements;

public sealed class SettlementsModel : PageModel
{
    private const string SettlementsPage = "/Admin/Settlements/Settlements";
    private const string DayKey = "day";
    private const string OperatorKey = "operatorId";
    private const string NoStore = "no-store";

    private readonly SettlementsHandler _handler;
    private readonly IClock _clock;

    public SettlementsModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new SettlementsHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The Day box as typed, or yesterday when the address names none.</summary>
    public string DayText { get; private set; } = string.Empty;

    public SettlementsView View { get; private set; } = new SettlementsView();

    /// <summary>Opens on yesterday; the address always carries the day, so a link or a reload shows the same page.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ReadDay();
        if (!Request.Query.ContainsKey(DayKey))
        {
            return RedirectToPage(SettlementsPage, new RouteValueDictionary { [DayKey] = DayText });
        }

        View = await _handler.ListAsync(DayText, CorrelationId.Get(HttpContext), cancellationToken);
        return Page();
    }

    /// <summary>
    /// "Download report": asks Reporting for a fresh link and sends the browser straight to it. A plain
    /// link rather than a form, so the page's form-action rule never meets the report's address.
    /// </summary>
    public async Task<IActionResult> OnGetDownloadAsync(CancellationToken cancellationToken)
    {
        ReadDay();
        string correlationId = CorrelationId.Get(HttpContext);
        DateOnly? day = ReportDisplay.ParseDay(DayText);
        string operatorId = Request.Query[OperatorKey].ToString().Trim();
        var outcome = new DownloadOutcome { Alert = PageAlert.Danger(SettlementsHandler.ReportNotAvailable) };
        if (day != null && operatorId.Length > 0)
        {
            outcome = await _handler.DownloadAsync(day.Value, operatorId, correlationId, cancellationToken);
        }

        if (outcome.Address != null)
        {
            Response.Headers.CacheControl = NoStore;
            return Redirect(outcome.Address);
        }

        View = await _handler.ListAsync(DayText, correlationId, cancellationToken);
        View = View with { Alert = outcome.Alert };
        return Page();
    }

    /// <summary>The address of one company's download for the day on show.</summary>
    public string DownloadAddress(Settlement settlement)
    {
        var values = new RouteValueDictionary { [DayKey] = DayText, [OperatorKey] = settlement.OperatorId };
        return Url.Page(SettlementsPage, "Download", values)!;
    }

    // A day missing from the address is yesterday; a box left empty stays empty and is told so.
    private void ReadDay()
    {
        if (!Request.Query.ContainsKey(DayKey))
        {
            DateOnly yesterday = IndiaTime.Today(_clock.UtcNow).AddDays(-1);
            DayText = ReportDisplay.WireDate(yesterday);
            return;
        }

        string? typed = Request.Query[DayKey];
        DayText = (typed ?? string.Empty).Trim();
    }
}
