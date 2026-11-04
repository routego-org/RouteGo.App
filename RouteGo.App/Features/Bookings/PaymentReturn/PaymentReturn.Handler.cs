namespace RouteGo.App.Features.Bookings.PaymentReturn;

/// <summary>What the payment return page shows for the booking as Booking last said it.</summary>
public enum PaymentPhase
{
    /// <summary>Booking is still waiting for the payment and the page is still within its minute.</summary>
    Confirming,

    /// <summary>Booking is still waiting after the whole minute: the customer is told an email will follow.</summary>
    StillConfirming,

    Confirmed,

    /// <summary>The booking failed: declined payment, lost seats, a service down.</summary>
    NotCompleted,

    Expired,

    Cancelled,

    /// <summary>The booking could not be shown: unknown, someone else's, or Booking not reachable.</summary>
    Problem,
}

/// <summary>One look at a booking for the payment return page.</summary>
public sealed record PaymentReturnQuery
{
    public required Guid BookingId { get; init; }

    /// <summary>Whole seconds since the page was first opened.</summary>
    public required int SecondsWaited { get; init; }

    public required DateTimeOffset Now { get; init; }

    public required string CorrelationId { get; init; }
}

/// <summary>What to draw: the phase, the booking when there is one, and whether to look again in two seconds.</summary>
public sealed record PaymentReturnView
{
    public required PaymentPhase Phase { get; init; }

    public BookingDetail? Booking { get; init; }

    public PageAlert? Alert { get; init; }

    /// <summary>The seconds left on the hold while confirming; null when Booking gives no future hold end.</summary>
    public int? HoldSecondsLeft { get; init; }

    public required bool KeepChecking { get; init; }
}

public sealed class PaymentReturnHandler
{
    /// <summary>How often the page looks at the booking again.</summary>
    public const int CheckEverySeconds = 2;

    /// <summary>How long after it was first opened the page keeps looking.</summary>
    public const int MaxWaitSeconds = 60;

    private readonly IHttpClientFactory _httpClientFactory;

    public PaymentReturnHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Loads the booking and decides what the page shows. The page never decides the result itself:
    /// it shows what Booking says, and only a booking still waiting, or Booking being out of reach,
    /// makes it look again.
    /// </summary>
    public async Task<PaymentReturnView> ViewAsync(PaymentReturnQuery query, CancellationToken cancellationToken)
    {
        ApiResult<BookingDetail> api = await BookingApi.GetBookingAsync(_httpClientFactory, query.BookingId, cancellationToken);
        bool canWait = query.SecondsWaited < MaxWaitSeconds;
        if (!api.IsSuccess)
        {
            return Refused(api, canWait, query.CorrelationId);
        }

        return Decide(api.Value!, query, canWait);
    }

    private static PaymentReturnView Refused(ApiResult<BookingDetail> api, bool canWait, string correlationId)
    {
        return new PaymentReturnView
        {
            Phase = PaymentPhase.Problem,
            Alert = BookingProblems.ToLoadAlert(api, correlationId),
            KeepChecking = canWait && BookingProblems.IsOutage(api.Problem),
        };
    }

    private static PaymentReturnView Decide(BookingDetail booking, PaymentReturnQuery query, bool canWait)
    {
        PaymentPhase phase = PhaseOf(booking.Status, canWait);
        int? holdSecondsLeft = null;
        if (phase == PaymentPhase.Confirming)
        {
            holdSecondsLeft = BookingDisplay.HoldSecondsLeft(booking.HoldExpiresAt, query.Now);
        }

        return new PaymentReturnView
        {
            Phase = phase,
            Booking = booking,
            HoldSecondsLeft = holdSecondsLeft,
            KeepChecking = phase == PaymentPhase.Confirming,
        };
    }

    private static PaymentPhase PhaseOf(string status, bool canWait)
    {
        switch (status)
        {
            case BookingDisplay.Confirmed:
                return PaymentPhase.Confirmed;
            case BookingDisplay.Failed:
                return PaymentPhase.NotCompleted;
            case BookingDisplay.Expired:
                return PaymentPhase.Expired;
            case BookingDisplay.Cancelled:
                return PaymentPhase.Cancelled;
            default:
                return WaitingPhase(canWait);
        }
    }

    private static PaymentPhase WaitingPhase(bool canWait)
    {
        if (canWait)
        {
            return PaymentPhase.Confirming;
        }

        return PaymentPhase.StillConfirming;
    }
}
