namespace RouteGo.App.Features.Tickets.Ticket;

/// <summary>What the ticket page does for a booking.</summary>
public enum TicketPhase
{
    /// <summary>The ticket is stored and the booking confirmed: send the browser to the PDF.</summary>
    Open,

    /// <summary>The booking is confirmed but its ticket is not stored yet and the page is still within its two minutes.</summary>
    Preparing,

    /// <summary>A message instead of a ticket: no such booking, no ticket for it, or a service out of reach.</summary>
    Message,
}

/// <summary>One look at a booking's ticket.</summary>
public sealed record TicketQuery
{
    public required Guid BookingId { get; init; }

    /// <summary>Whole seconds since the page was first opened.</summary>
    public required int SecondsWaited { get; init; }

    public required DateTimeOffset Now { get; init; }

    public required string CorrelationId { get; init; }
}

/// <summary>What to do: go to the PDF, wait and look again, or show a message.</summary>
public sealed record TicketView
{
    public required TicketPhase Phase { get; init; }

    /// <summary>The link to the PDF; only when <see cref="Phase"/> is <see cref="TicketPhase.Open"/>. Never shown on a page.</summary>
    public string? Link { get; init; }

    public PageAlert? Alert { get; init; }
}

public sealed class TicketPageHandler
{
    /// <summary>How often the page looks for the ticket again.</summary>
    public const int CheckEverySeconds = 5;

    /// <summary>How long after it was first opened the page keeps looking.</summary>
    public const int MaxWaitSeconds = 120;

    public const string Cancelled = "This booking was cancelled, so its ticket can no longer be opened.";
    public const string NotConfirmedYet = "This booking is not confirmed yet. Your ticket will be ready once payment is confirmed.";
    public const string NoTicket = "This booking was not confirmed, so it has no ticket.";
    public const string TakingLonger = "Your ticket is taking longer than usual. Please try again in a few minutes. Your booking is confirmed.";

    private const string HomeAddress = "/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITicketStorage _storage;
    private readonly ILogger _logger;

    public TicketPageHandler(IHttpClientFactory httpClientFactory, ITicketStorage storage, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>The page for an address that is no booking identifier: Booking is not asked.</summary>
    public static TicketView NotFound()
    {
        return Message(PageAlert.Danger(Messages.ProblemNotFound("booking")));
    }

    /// <summary>
    /// Asks Booking who may open the booking and whether it is confirmed, and only for a confirmed
    /// booking looks in ticket storage. The status decides first: a cancelled booking never opens its
    /// ticket even though it is still stored.
    /// </summary>
    public async Task<TicketView> ViewAsync(TicketQuery query, CancellationToken cancellationToken)
    {
        ApiResult<BookingDetail> api = await BookingApi.GetBookingAsync(_httpClientFactory, query.BookingId, cancellationToken);
        if (!api.IsSuccess)
        {
            return Refused(api, query.CorrelationId);
        }

        if (api.Value!.Status != BookingDisplay.Confirmed)
        {
            return Message(NoTicketAlert(api.Value.Status));
        }

        TicketPresence presence = await _storage.CheckAsync(query.BookingId, cancellationToken);
        return Decide(presence, query);
    }

    private TicketView Decide(TicketPresence presence, TicketQuery query)
    {
        switch (presence)
        {
            case TicketPresence.Stored:
                _logger.LogInformation("Ticket link made for booking {BookingId}", query.BookingId);
                return new TicketView { Phase = TicketPhase.Open, Link = _storage.MakeLink(query.BookingId, query.Now) };
            case TicketPresence.NotStored:
                return NotStoredYet(query);
            default:
                return Message(PageAlert.Danger(Messages.ProblemUpstream));
        }
    }

    private static TicketView NotStoredYet(TicketQuery query)
    {
        if (query.SecondsWaited < MaxWaitSeconds)
        {
            return new TicketView { Phase = TicketPhase.Preparing };
        }

        PageAlert alert = PageAlert.Warning(TakingLonger).WithLink(Messages.LinkTryAgain, BookingDisplay.TicketAddress(query.BookingId));
        return Message(alert);
    }

    private static TicketView Refused(ApiResult<BookingDetail> api, string correlationId)
    {
        ApiProblem? problem = api.Problem;
        if (problem is null)
        {
            return Message(PageAlert.Danger(Messages.ProblemInternalError(correlationId)));
        }

        if (problem.Code == BookingProblems.NotFound)
        {
            return NotFound();
        }

        return Message(SharedProblems.ToAlert(problem, correlationId));
    }

    private static PageAlert NoTicketAlert(string status)
    {
        switch (status)
        {
            case BookingDisplay.Cancelled:
                return PageAlert.Warning(Cancelled);
            case BookingDisplay.Failed:
            case BookingDisplay.Expired:
                return PageAlert.Warning(NoTicket);
            default:
                return PageAlert.Info(NotConfirmedYet);
        }
    }

    // Every message page offers the way home.
    private static TicketView Message(PageAlert alert)
    {
        return new TicketView { Phase = TicketPhase.Message, Alert = alert.WithLink(Messages.AccessGoHome, HomeAddress) };
    }
}
