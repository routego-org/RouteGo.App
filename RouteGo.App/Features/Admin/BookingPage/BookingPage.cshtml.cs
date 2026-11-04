using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace RouteGo.App.Features.Admin.BookingPage;

/// <summary>The force-cancel confirmation's boxes; the contact and total travel along so the question can be asked again when Booking cannot be reached.</summary>
public sealed class ForceCancelInput
{
    public string? Reason { get; set; }

    public string? RefundPercent { get; set; }

    public string? ContactName { get; set; }

    public int Total { get; set; }
}

public sealed class BookingPageModel : PageModel
{
    private const string BackKey = "back";
    private const string SearchAddress = "/admin/bookings";
    private const string ReasonField = "Input.Reason";
    private const string PercentField = "Input.RefundPercent";

    private static readonly string[] _searchKeys = new string[] { "bookingId", "email", "tripId", "page" };

    private readonly BookingPageHandler _handler;
    private readonly IClock _clock;

    public BookingPageModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new BookingPageHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The booking named in the address, as typed: an address that is no identifier is told so, never sent.</summary>
    [BindProperty(SupportsGet = true)]
    public string? BookingId { get; set; }

    /// <summary>Set by "Force-cancel": the page asks first, above the booking.</summary>
    [BindProperty(SupportsGet = true, Name = "forceCancel")]
    public bool IsForceCancelling { get; set; }

    [BindProperty]
    public ForceCancelInput Input { get; set; } = new ForceCancelInput();

    public AdminBookingView? View { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>Whether the force-cancel confirmation is shown.</summary>
    public bool IsQuestionOpen { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await ShowBookingAsync(cancellationToken);
        IsQuestionOpen = IsForceCancelling && View != null && View.IsConfirmed;
    }

    public async Task<IActionResult> OnPostForceCancelAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingPageHandler.NotFoundAlert(BackAddress());
            return Page();
        }

        ForceCancelCommand? command = await CheckInputAsync(bookingId.Value, cancellationToken);
        if (command is null)
        {
            return Page();
        }

        ForceCancelOutcome outcome = await _handler.ForceCancelAsync(command, BackAddress(), CorrelationId.Get(HttpContext), cancellationToken);
        if (outcome.Success != null)
        {
            TempData[Flash.Key] = outcome.Success;
            return RedirectToPage(new { bookingId = BookingId, back = BackParameter() });
        }

        await ShowOutcomeAsync(outcome, cancellationToken);
        return Page();
    }

    /// <summary>"Booking 77777777-…".</summary>
    public string Heading()
    {
        return $"Booking {BookingId}";
    }

    /// <summary>The booking page itself, keeping the search it was opened from.</summary>
    public string ThisAddress()
    {
        return PageAddress(null);
    }

    /// <summary>The booking page with the force-cancel confirmation open.</summary>
    public string QuestionAddress()
    {
        return PageAddress(new KeyValuePair<string, string?>("forceCancel", "true"));
    }

    /// <summary>Where the confirmation posts, keeping the search.</summary>
    public string ForceCancelAddress()
    {
        return PageAddress(new KeyValuePair<string, string?>("handler", "ForceCancel"));
    }

    /// <summary>The search the admin came from when it is known (only its own criteria and page are kept), otherwise the bare search.</summary>
    public string BackAddress()
    {
        string? back = BackParameter();
        if (back is null)
        {
            return SearchAddress;
        }

        return $"{SearchAddress}?{back}";
    }

    public string HistoryAddress()
    {
        return $"/admin/audit-log?kind=Booking&id={Uri.EscapeDataString(View!.Booking.BookingId.ToString())}";
    }

    public string SeatsAddress()
    {
        BookingTrip trip = View!.Booking.Trip;
        return SeatDisplay.WithRoute($"/admin/trips/{trip.TripId}/seats", BookingDisplay.RouteName(trip));
    }

    /// <summary>The question the confirmation asks, with the refund words following what is typed.</summary>
    public string Question()
    {
        Guid bookingId = BookingDisplay.ParseBookingId(BookingId) ?? Guid.Empty;
        string contactName = Input.ContactName ?? string.Empty;
        int total = Input.Total;
        if (View != null)
        {
            contactName = View.Booking.ContactName;
            total = View.Booking.TotalAmount;
        }

        return BookingPageHandler.Question(bookingId, contactName, total, Input.RefundPercent ?? string.Empty);
    }

    /// <summary>The contact the confirmation names, from the loaded booking or the posted boxes.</summary>
    public string QuestionContactName()
    {
        if (View != null)
        {
            return View.Booking.ContactName;
        }

        return Input.ContactName ?? string.Empty;
    }

    public int QuestionTotal()
    {
        if (View != null)
        {
            return View.Booking.TotalAmount;
        }

        return Input.Total;
    }

    private async Task ShowBookingAsync(CancellationToken cancellationToken)
    {
        Guid? bookingId = BookingDisplay.ParseBookingId(BookingId);
        if (bookingId is null)
        {
            Alert = BookingPageHandler.NotFoundAlert(BackAddress());
            return;
        }

        ApiResult<BookingDetail> api = await _handler.GetBookingAsync(bookingId.Value, cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = BookingPageHandler.LoadAlert(api, BackAddress(), CorrelationId.Get(HttpContext));
            return;
        }

        View = BookingPageHandler.Decide(api.Value!, _clock.UtcNow);
    }

    // The command to send when the boxes pass the page's own checks; otherwise the confirmation is shown again with the messages.
    private async Task<ForceCancelCommand?> CheckInputAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        string reason = (Input.Reason ?? string.Empty).Trim();
        string percentText = (Input.RefundPercent ?? string.Empty).Trim();
        string? reasonError = BookingPageHandler.ReasonError(reason);
        string? percentError = BookingPageHandler.PercentError(percentText);
        if (reasonError is null && percentError is null)
        {
            return new ForceCancelCommand(bookingId, reason, BookingPageHandler.ParsePercent(percentText));
        }

        await ShowBookingAsync(cancellationToken);
        ShowFieldErrors(reasonError, percentError);
        return null;
    }

    private async Task ShowOutcomeAsync(ForceCancelOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.ShouldReload)
        {
            await ShowBookingAsync(cancellationToken);
        }

        ShowFieldErrors(outcome.ReasonError, outcome.PercentError);
        if (outcome.Alert != null)
        {
            Alert = outcome.Alert;
        }

        IsQuestionOpen = outcome.KeepsQuestion;
    }

    private void ShowFieldErrors(string? reasonError, string? percentError)
    {
        IsQuestionOpen = View != null && View.IsConfirmed;
        if (reasonError != null)
        {
            ModelState.AddModelError(ReasonField, reasonError);
        }

        if (percentError != null)
        {
            ModelState.AddModelError(PercentField, percentError);
        }
    }

    // Only the search page's own criteria and page come back; anything else in "back" is dropped.
    private string? BackParameter()
    {
        string? typed = Request.Query[BackKey];
        if (string.IsNullOrEmpty(typed))
        {
            return null;
        }

        Dictionary<string, StringValues> parsed = QueryHelpers.ParseQuery(typed);
        var kept = new List<string>();
        foreach (string key in _searchKeys)
        {
            if (parsed.ContainsKey(key))
            {
                kept.Add($"{key}={Uri.EscapeDataString(parsed[key].ToString())}");
            }
        }

        if (kept.Count == 0)
        {
            return null;
        }

        return string.Join('&', kept);
    }

    private string PageAddress(KeyValuePair<string, string?>? extra)
    {
        var parameters = new Dictionary<string, string?>();
        if (extra != null)
        {
            parameters[extra.Value.Key] = extra.Value.Value;
        }

        string? back = BackParameter();
        if (back != null)
        {
            parameters[BackKey] = back;
        }

        return QueryHelpers.AddQueryString($"/admin/bookings/{BookingId}", parameters);
    }
}
