namespace RouteGo.App.Features.Bookings.Booking;

public sealed class BookingModel : PageModel
{
    private const string GaveUp = "You gave up this booking. Your seats were released.";
    private const string BookingNotUnpaid = "BOOKING_NOT_UNPAID";

    private readonly BookingPageHandler _handler;
    private readonly IClock _clock;

    public BookingModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new BookingPageHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The booking named in the address, as typed: an address that is no identifier is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? BookingId { get; set; }

    /// <summary>Set by "Give up this booking": the page asks first, above the booking.</summary>
    [BindProperty(SupportsGet = true, Name = "giveUp")]
    public bool IsGivingUp { get; set; }

    public BookingPageView? View { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await ShowBookingAsync(cancellationToken);
    }

    /// <summary>"Pay now": loads the booking again and sends the browser to the payment page address Booking gives now.</summary>
    public async Task<IActionResult> OnPostPayNowAsync(CancellationToken cancellationToken)
    {
        await ShowBookingAsync(cancellationToken);
        string? address = null;
        if (View != null && View.CanPayNow)
        {
            address = BookingDisplay.PaymentAddress(View.Booking.CheckoutUrl);
        }

        if (address is null)
        {
            return Page();
        }

        return Redirect(address);
    }

    public async Task<IActionResult> OnPostGiveUpAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingProblems.NotFoundAlert();
            return Page();
        }

        ApiResult<BookingDetail> api = await _handler.ReleaseAsync(bookingId.Value, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = GaveUp;
            return RedirectToPage(new { bookingId = BookingId });
        }

        await ShowBookingAsync(cancellationToken);
        Alert = ToRefusalAlert(api);
        return Page();
    }

    /// <summary>"Booking 77777777-…".</summary>
    public string Heading()
    {
        return $"Booking {View!.Booking.BookingId}";
    }

    public string ThisAddress()
    {
        return BookingDisplay.BookingAddress(View!.Booking.BookingId);
    }

    /// <summary>Whether to ask "Give up this booking?" above the booking: asked for, and the booking is still waiting.</summary>
    public bool IsAskingToGiveUp()
    {
        return IsGivingUp && View != null && View.IsWaiting;
    }

    private async Task ShowBookingAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingProblems.NotFoundAlert();
            return;
        }

        ApiResult<BookingDetail> api = await _handler.GetBookingAsync(bookingId.Value, cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = BookingProblems.ToLoadAlert(api, CorrelationId.Get(HttpContext));
            return;
        }

        View = BookingPageHandler.Decide(api.Value!, _clock.UtcNow);
    }

    // A booking that was paid or ended meanwhile is told so; the booking is already loaded again.
    private PageAlert ToRefusalAlert(ApiResult<BookingDetail> api)
    {
        ApiProblem? problem = api.Problem;
        if (problem != null && problem.Code == BookingNotUnpaid)
        {
            string status = problem.GetExtension("bookingStatus") ?? string.Empty;
            return PageAlert.Info($"This booking is already {BookingDisplay.StatusLabel(status)}. Nothing was changed.");
        }

        return BookingProblems.ToLoadAlert(api, CorrelationId.Get(HttpContext));
    }
}
