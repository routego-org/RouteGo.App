namespace RouteGo.App.Features.Bookings.CancelBooking;

/// <summary>What Booking says the customer would get back if they cancelled now.</summary>
public sealed record CancellationPreview
{
    public required int TotalAmount { get; init; }

    public required int RefundPercent { get; init; }

    public required int RefundAmount { get; init; }

    /// <summary>The moment the refund share was worked out; cancelling later may give a lower one.</summary>
    public required DateTimeOffset CalculatedAt { get; init; }
}

/// <summary>Booking's answer to a cancellation.</summary>
public sealed record BookingCancellationAnswer
{
    public required int RefundPercent { get; init; }

    public required int RefundAmount { get; init; }

    /// <summary>requested or completed; null when no refund is due.</summary>
    public string? RefundStatus { get; init; }
}

/// <summary>What the page says once the booking is cancelled; carried across the redirect.</summary>
public sealed record CancelledSummary
{
    public required int RefundPercent { get; init; }

    public required int RefundAmount { get; init; }

    public string? RefundStatus { get; init; }

    /// <summary>The share the customer was shown before cancelling.</summary>
    public required int ShownPercent { get; init; }
}

/// <summary>The cancel page as loaded: the preview with its booking, or the message that says why there is none.</summary>
public sealed record CancelPageView
{
    public BookingDetail? Booking { get; init; }

    public CancellationPreview? Preview { get; init; }

    public PageAlert? Alert { get; init; }
}

/// <summary>What a cancel attempt came to: the booking is cancelled, or a message, and whether to load the page again.</summary>
public sealed record CancelOutcome
{
    public CancelledSummary? Summary { get; init; }

    public PageAlert? Alert { get; init; }

    public bool ShouldReload { get; init; }
}

public sealed class CancelBookingHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public CancelBookingHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Loads the booking and the refund preview together; a booking Booking refuses is shown as that
    /// refusal, and a preview it refuses (not confirmed, trip left) takes the place of the preview.
    /// </summary>
    public async Task<CancelPageView> ViewAsync(Guid bookingId, string correlationId, CancellationToken cancellationToken)
    {
        Task<ApiResult<BookingDetail>> bookingTask = BookingApi.GetBookingAsync(_httpClientFactory, bookingId, cancellationToken);
        Task<ApiResult<CancellationPreview>> previewTask = PreviewAsync(bookingId, cancellationToken);
        await Task.WhenAll(bookingTask, previewTask);
        ApiResult<BookingDetail> booking = await bookingTask;
        ApiResult<CancellationPreview> preview = await previewTask;
        if (!booking.IsSuccess)
        {
            return new CancelPageView { Alert = BookingProblems.ToLoadAlert(booking, correlationId) };
        }

        if (!preview.IsSuccess)
        {
            return new CancelPageView { Booking = booking.Value, Alert = RefusalAlert(preview, bookingId, correlationId) };
        }

        return new CancelPageView { Booking = booking.Value, Preview = preview.Value };
    }

    /// <summary>
    /// Cancels the booking. Never sent twice: a write is not retried, and when Booking cannot be
    /// reached the page says so and points to the booking, where the person sees whether it happened.
    /// </summary>
    public async Task<CancelOutcome> CancelAsync(Guid bookingId, int shownPercent, string correlationId, CancellationToken cancellationToken)
    {
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Post, $"bookings/{bookingId}/cancel");
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        ApiResult<BookingCancellationAnswer> api = await response.ReadApiResultAsync<BookingCancellationAnswer>(cancellationToken);
        if (api.IsSuccess)
        {
            return new CancelOutcome { Summary = ToSummary(api.Value!, shownPercent) };
        }

        if (BookingProblems.IsOutage(api.Problem))
        {
            var outage = PageAlert.Danger(Messages.ProblemUpstream);
            return new CancelOutcome { Alert = outage.WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId)) };
        }

        return new CancelOutcome { Alert = RefusalAlert(api, bookingId, correlationId), ShouldReload = true };
    }

    /// <summary>Whether the refund came out lower than the share the customer was shown.</summary>
    public static bool IsLowerThanShown(CancelledSummary summary)
    {
        return summary.RefundPercent < summary.ShownPercent;
    }

    private async Task<ApiResult<CancellationPreview>> PreviewAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Get, $"bookings/{bookingId}/cancellationPreview");
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<CancellationPreview>(cancellationToken);
    }

    private static CancelledSummary ToSummary(BookingCancellationAnswer answer, int shownPercent)
    {
        return new CancelledSummary
        {
            RefundPercent = answer.RefundPercent,
            RefundAmount = answer.RefundAmount,
            RefundStatus = answer.RefundStatus,
            ShownPercent = shownPercent,
        };
    }

    // The refusals of the preview and of the cancel itself are the same two, so they share their words.
    private static PageAlert RefusalAlert<T>(ApiResult<T> api, Guid bookingId, string correlationId)
    {
        ApiProblem? problem = api.Problem;
        if (problem != null && problem.Code == BookingProblems.BookingNotConfirmed)
        {
            string status = problem.GetExtension("bookingStatus") ?? string.Empty;
            return BookingProblems.NotConfirmedAlert(status, bookingId);
        }

        if (problem != null && problem.Code == BookingProblems.TripDeparted)
        {
            return PageAlert.Danger(Messages.ProblemTripDeparted).WithLink(BookingDisplay.LinkViewBooking, BookingDisplay.BookingAddress(bookingId));
        }

        return BookingProblems.ToLoadAlert(api, correlationId);
    }
}
