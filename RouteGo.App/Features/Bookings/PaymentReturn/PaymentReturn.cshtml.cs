namespace RouteGo.App.Features.Bookings.PaymentReturn;

public sealed class PaymentReturnModel : PageModel
{
    private readonly PaymentReturnHandler _handler;
    private readonly IClock _clock;

    public PaymentReturnModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new PaymentReturnHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The booking named in the address, as typed: an address that is no identifier is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? BookingId { get; set; }

    /// <summary>
    /// When this visit started, in Unix seconds, as the page's own refresh address carried it. Opening
    /// the page without it — by hand or by "Try again" — starts a new minute.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "since")]
    public long? Since { get; set; }

    public PaymentReturnView? View { get; private set; }

    /// <summary>The message shown when there is no booking to show.</summary>
    public PageAlert? Alert { get; private set; }

    /// <summary>Where the browser looks again in two seconds; empty when the page is done checking.</summary>
    public string RefreshAddress { get; private set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingProblems.NotFoundAlert();
            return;
        }

        DateTimeOffset now = _clock.UtcNow;
        long startedAt = StartedAt(now);
        var query = new PaymentReturnQuery
        {
            BookingId = bookingId.Value,
            SecondsWaited = (int)(now.ToUnixTimeSeconds() - startedAt),
            Now = now,
            CorrelationId = CorrelationId.Get(HttpContext),
        };
        View = await _handler.ViewAsync(query, cancellationToken);
        Alert = View.Alert;
        if (View.KeepChecking)
        {
            RefreshAddress = $"{PageAddress()}?since={startedAt}";
        }
    }

    /// <summary>"Confirming your payment" while waiting, "Booking confirmed" once it is, and a plain name otherwise.</summary>
    public string Title()
    {
        if (View is null)
        {
            return "Your booking";
        }

        switch (View.Phase)
        {
            case PaymentPhase.Confirming:
                return "Confirming your payment";
            case PaymentPhase.Confirmed:
                return "Booking confirmed";
            default:
                return "Your booking";
        }
    }

    /// <summary>The page without a start time, which begins another minute of checking.</summary>
    public string PageAddress()
    {
        return $"/my-bookings/{BookingId}/payment-return";
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
