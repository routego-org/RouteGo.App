namespace RouteGo.App.Features.Bookings.CancelBooking;

public sealed class CancelBookingModel : PageModel
{
    private const string SummaryKey = "CancelledSummary";

    private readonly CancelBookingHandler _handler;

    public CancelBookingModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new CancelBookingHandler(httpClientFactory);
    }

    /// <summary>The booking named in the address, as typed: an address that is no identifier is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? BookingId { get; set; }

    /// <summary>Set after a cancellation went through: the page shows what came of it.</summary>
    [BindProperty(SupportsGet = true, Name = "cancelled")]
    public bool IsCancelled { get; set; }

    /// <summary>The refund share the preview showed, sent back so the page can say when the refund came out lower.</summary>
    [BindProperty]
    public int PreviewPercent { get; set; }

    public CancelPageView? View { get; private set; }

    public CancelledSummary? Done { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (IsCancelled && TryTakeSummary())
        {
            return;
        }

        await ShowPreviewAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingProblems.NotFoundAlert();
            return Page();
        }

        CancelOutcome outcome = await _handler.CancelAsync(bookingId.Value, PreviewPercent, CorrelationId.Get(HttpContext), cancellationToken);
        if (outcome.Summary != null)
        {
            TempData[SummaryKey] = JsonSerializer.Serialize(outcome.Summary, WireJson.Options);
            return RedirectToPage(new { bookingId = BookingId, cancelled = true });
        }

        if (outcome.ShouldReload)
        {
            await ShowPreviewAsync(cancellationToken);
        }

        if (View?.Alert is null)
        {
            Alert = outcome.Alert;
        }

        return Page();
    }

    /// <summary>"Cancel booking: Mumbai → Pune, 31 Oct 2026, 5:00 PM IST", or "Cancel booking" when no booking is shown.</summary>
    public string Heading()
    {
        if (View?.Booking is null)
        {
            return "Cancel booking";
        }

        return $"Cancel booking: {BookingDisplay.RouteAndDeparture(View.Booking.Trip)}";
    }

    public string BookingAddress()
    {
        return $"/my-bookings/{BookingId}";
    }

    private async Task ShowPreviewAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingProblems.NotFoundAlert();
            return;
        }

        View = await _handler.ViewAsync(bookingId.Value, CorrelationId.Get(HttpContext), cancellationToken);
        Alert = View.Alert;
        PreviewPercent = 0;
        if (View.Preview != null)
        {
            PreviewPercent = View.Preview.RefundPercent;
        }
    }

    // The summary survives one redirect only; a refreshed page falls back to the normal load.
    private bool TryTakeSummary()
    {
        if (BookingDisplay.ParseBookingId(BookingId) is null || TempData[SummaryKey] is not string json)
        {
            return false;
        }

        Done = JsonSerializer.Deserialize<CancelledSummary>(json, WireJson.Options);
        return Done != null;
    }
}
