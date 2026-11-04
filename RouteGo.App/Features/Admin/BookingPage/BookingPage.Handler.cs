using System.Globalization;

namespace RouteGo.App.Features.Admin.BookingPage;

/// <summary>What the admin booking page offers for a booking as loaded.</summary>
public sealed record AdminBookingView
{
    public required BookingDetail Booking { get; init; }

    public bool IsWaiting { get; init; }

    /// <summary>The seconds left on the hold while waiting; null when none is given or it has passed.</summary>
    public int? HoldSecondsLeft { get; init; }

    /// <summary>Only a confirmed booking can be force-cancelled or has a ticket.</summary>
    public bool IsConfirmed { get; init; }
}

/// <summary>Booking's answer to a force-cancel: what was refunded and how far that has got.</summary>
public sealed record ForceCancellation
{
    public required int RefundPercent { get; init; }

    public required int RefundAmount { get; init; }

    /// <summary>requested or completed; null when no refund is due.</summary>
    public string? RefundStatus { get; init; }
}

/// <summary>What a force-cancel came to: a success message, or why not, and what the page does next.</summary>
public sealed record ForceCancelOutcome
{
    /// <summary>Set when the booking was force-cancelled just now.</summary>
    public string? Success { get; init; }

    public PageAlert? Alert { get; init; }

    /// <summary>Booking refused the reason or the percentage; the confirmation stays open with these messages.</summary>
    public string? ReasonError { get; init; }

    public string? PercentError { get; init; }

    /// <summary>Whether the confirmation stays open with what was typed (Booking out of reach, or its refusal of the boxes).</summary>
    public bool KeepsQuestion { get; init; }

    /// <summary>Whether the booking is loaded again; not when it is gone, not allowed, or Booking cannot be reached.</summary>
    public bool ShouldReload { get; init; }
}

/// <summary>What the admin typed to force-cancel a booking; no percentage means a full refund.</summary>
public sealed record ForceCancelCommand(Guid BookingId, string Reason, int? RefundPercent);

public sealed class BookingPageHandler
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;

    public const string AlreadyCancelled = "RouteGo already cancelled this booking. Nothing was changed.";
    public const string BackToSearch = "Back to search";

    private const string WindowClosed = "FORCE_CANCEL_WINDOW_CLOSED";

    private readonly IHttpClientFactory _httpClientFactory;

    public BookingPageHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The booking in full; an admin may read any booking.</summary>
    public async Task<ApiResult<BookingDetail>> GetBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        return await BookingApi.GetBookingAsync(_httpClientFactory, bookingId, cancellationToken);
    }

    /// <summary>
    /// Cancels a confirmed booking with the admin's reason and, when typed, a refund percentage. Never
    /// sent twice: a write is not retried. Booking decides whether the 7-day window is still open.
    /// </summary>
    public async Task<ForceCancelOutcome> ForceCancelAsync(ForceCancelCommand command, string backToSearch, string correlationId, CancellationToken cancellationToken)
    {
        var body = new ForceCancelRequest(command.Reason, command.RefundPercent);
        var request = new HttpRequestMessage(HttpMethod.Post, $"bookings/{command.BookingId}/forceCancellations")
        {
            Content = JsonContent.Create(body, options: WireJson.Options),
        };
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        bool isNew = response.StatusCode == System.Net.HttpStatusCode.Created;
        ApiResult<ForceCancellation> api = await response.ReadApiResultAsync<ForceCancellation>(cancellationToken);
        if (api.IsSuccess && isNew)
        {
            return new ForceCancelOutcome { Success = SuccessText(api.Value!), ShouldReload = true };
        }

        if (api.IsSuccess)
        {
            return new ForceCancelOutcome { Alert = PageAlert.Info(AlreadyCancelled), ShouldReload = true };
        }

        return Refusal(api, backToSearch, correlationId);
    }

    /// <summary>The decisions the page makes about a booking as loaded at <paramref name="now"/>.</summary>
    public static AdminBookingView Decide(BookingDetail booking, DateTimeOffset now)
    {
        if (BookingDisplay.IsWaiting(booking.Status))
        {
            int? secondsLeft = BookingDisplay.HoldSecondsLeft(booking.HoldExpiresAt, now);
            return new AdminBookingView { Booking = booking, IsWaiting = true, HoldSecondsLeft = secondsLeft };
        }

        return new AdminBookingView { Booking = booking, IsConfirmed = booking.Status == BookingDisplay.Confirmed };
    }

    /// <summary>The alert when the booking cannot be shown: unknown (or not an identifier), not allowed, or any shared problem.</summary>
    public static PageAlert LoadAlert<T>(ApiResult<T> api, string backToSearch, string correlationId)
    {
        ApiProblem? problem = api.Problem;
        if (problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(correlationId));
        }

        if (problem.Code == BookingProblems.NotFound)
        {
            return NotFoundAlert(backToSearch);
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }

    /// <summary>"We couldn't find that booking…" with the way back to the search.</summary>
    public static PageAlert NotFoundAlert(string backToSearch)
    {
        return PageAlert.Danger(Messages.ProblemNotFound("booking")).WithLink(BackToSearch, backToSearch);
    }

    /// <summary>
    /// The confirmation's question. The refund words follow what is typed in the percentage box,
    /// "in full" when it is empty or not yet a valid percentage.
    /// </summary>
    public static string Question(Guid bookingId, string contactName, int total, string percentText)
    {
        string refund = RefundText(percentText, total);
        return $"Force-cancel booking {bookingId} for {contactName}? Its seats go back on sale and the customer is refunded {refund}. This cannot be undone.";
    }

    /// <summary>"in full" with no percentage, otherwise "50% of ₹1,099".</summary>
    public static string RefundText(string percentText, int total)
    {
        int? percent = ParsePercent(percentText);
        if (percent is null || percentText.Trim().Length == 0)
        {
            return "in full";
        }

        return $"{percent}% of {TripDisplay.Rupees(total)}";
    }

    /// <summary>The whole number 0 to 100 a box holds; null when it is empty or not a number in range.</summary>
    public static int? ParsePercent(string text)
    {
        bool isNumber = long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long percent);
        if (!isNumber || percent < 0 || percent > 100)
        {
            return null;
        }

        return (int)percent;
    }

    /// <summary>The page's own check of the reason, trimmed; the message when it is out of range, otherwise null.</summary>
    public static string? ReasonError(string reason)
    {
        int length = reason.Trim().Length;
        if (length < MinReasonLength || length > MaxReasonLength)
        {
            return Messages.ValidationReasonLength;
        }

        return null;
    }

    /// <summary>The page's own check of the percentage box; null when it is empty or a whole number 0 to 100.</summary>
    public static string? PercentError(string text)
    {
        string typed = text.Trim();
        if (typed.Length == 0)
        {
            return null;
        }

        bool isNumber = long.TryParse(typed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long percent);
        if (!isNumber)
        {
            return Messages.ValidationFormat("refund percentage");
        }

        if (percent < 0 || percent > 100)
        {
            return Messages.ValidationRange("Refund percentage", 0, 100);
        }

        return null;
    }

    // "Booking force-cancelled. Refund: 100% — ₹1,600. Refund requested." or "…No refund is due."
    private static string SuccessText(ForceCancellation answer)
    {
        if (answer.RefundAmount == 0)
        {
            return "Booking force-cancelled. No refund is due.";
        }

        string text = $"Booking force-cancelled. Refund: {answer.RefundPercent}% — {TripDisplay.Rupees(answer.RefundAmount)}.";
        if (answer.RefundStatus is null)
        {
            return text;
        }

        return $"{text} {BookingDisplay.RefundStatusLabel(answer.RefundStatus)}.";
    }

    private static ForceCancelOutcome Refusal(ApiResult<ForceCancellation> api, string backToSearch, string correlationId)
    {
        if (api.Errors != null)
        {
            return FieldRefusal(api.Errors);
        }

        ApiProblem? problem = api.Problem;
        if (BookingProblems.IsOutage(problem))
        {
            return new ForceCancelOutcome { Alert = PageAlert.Danger(Messages.ProblemUpstream), KeepsQuestion = true };
        }

        if (problem != null && (problem.Code == BookingProblems.NotFound || problem.Code == SharedProblems.Forbidden))
        {
            return new ForceCancelOutcome { Alert = LoadAlert(api, backToSearch, correlationId) };
        }

        return new ForceCancelOutcome { Alert = ConflictAlert(api, backToSearch, correlationId), ShouldReload = true };
    }

    // Booking's own refusal names the box it did not like; the confirmation stays open under it.
    private static ForceCancelOutcome FieldRefusal(IReadOnlyDictionary<string, string[]> errors)
    {
        string? reasonError = null;
        string? percentError = null;
        if (errors.ContainsKey("reason"))
        {
            reasonError = Messages.ValidationReasonLength;
        }

        if (errors.ContainsKey("refundPercent"))
        {
            percentError = Messages.ValidationRange("Refund percentage", 0, 100);
        }

        return new ForceCancelOutcome { ReasonError = reasonError, PercentError = percentError, KeepsQuestion = true, ShouldReload = true };
    }

    private static PageAlert ConflictAlert(ApiResult<ForceCancellation> api, string backToSearch, string correlationId)
    {
        ApiProblem? problem = api.Problem;
        if (problem != null && problem.Code == BookingProblems.BookingNotConfirmed)
        {
            string status = problem.GetExtension("bookingStatus") ?? string.Empty;
            return PageAlert.Danger(BookingProblems.NotConfirmedText(status));
        }

        if (problem != null && problem.Code == WindowClosed)
        {
            return PageAlert.Danger(WindowClosedText(problem));
        }

        return LoadAlert(api, backToSearch, correlationId);
    }

    // "The trip left on {departed_at}, and a booking can be force-cancelled up to {window_days} days after that."
    private static string WindowClosedText(ApiProblem problem)
    {
        string? departedAt = IndiaTime.FormatIso(problem.GetExtension("departedAt"));
        string days = problem.GetExtension("windowDays") ?? "7";
        return $"This booking is too old to force-cancel. The trip left on {departedAt}, and a booking can be force-cancelled up to {days} days after that. Nothing was changed.";
    }

    private sealed record ForceCancelRequest(string Reason, int? RefundPercent);
}
