namespace RouteGo.App.Features.Tickets.Ticket;

public sealed class TicketModel : PageModel
{
    private readonly TicketPageHandler _handler;
    private readonly IClock _clock;

    public TicketModel(IHttpClientFactory httpClientFactory, ITicketStorage storage, IClock clock, ILogger<TicketModel> logger)
    {
        _handler = new TicketPageHandler(httpClientFactory, storage, logger);
        _clock = clock;
    }

    /// <summary>The booking named in the address, as typed: an address that is no identifier is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? BookingId { get; set; }

    /// <summary>
    /// When this visit started, in Unix seconds, as the page's own refresh address carried it. Opening
    /// the page without it — by hand or by "Try again" — starts a new two minutes.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "since")]
    public long? Since { get; set; }

    public TicketView? View { get; private set; }

    /// <summary>Where the browser looks again in five seconds; empty when the page is done checking.</summary>
    public string RefreshAddress { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            View = TicketPageHandler.NotFound();
            return Page();
        }

        DateTimeOffset now = _clock.UtcNow;
        long startedAt = StartedAt(now);
        var query = new TicketQuery
        {
            BookingId = bookingId.Value,
            SecondsWaited = (int)(now.ToUnixTimeSeconds() - startedAt),
            Now = now,
            CorrelationId = CorrelationId.Get(HttpContext),
        };
        View = await _handler.ViewAsync(query, cancellationToken);
        if (View.Phase == TicketPhase.Open)
        {
            // The link is single-purpose and short-lived: no browser or proxy may keep or replay it.
            Response.Headers.CacheControl = "no-store";
            return Redirect(View.Link!);
        }

        if (View.Phase == TicketPhase.Preparing)
        {
            RefreshAddress = $"{BookingDisplay.TicketAddress(bookingId.Value)}?since={startedAt}";
        }

        return Page();
    }

    private long StartedAt(DateTimeOffset now)
    {
        long nowSeconds = now.ToUnixTimeSeconds();
        if (Since is null || Since.Value > nowSeconds || Since.Value < 0)
        {
            return nowSeconds;
        }

        return Since.Value;
    }
}
