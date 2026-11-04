using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace RouteGo.App.Features.Admin.Bookings;

/// <summary>One line of Booking's admin search: a booking in any status with its contact, trip, seats and total.</summary>
public sealed record AdminBookingSummary
{
    public required Guid BookingId { get; init; }

    public required string Status { get; init; }

    public required string ContactName { get; init; }

    public required string ContactEmail { get; init; }

    public required BookingTrip Trip { get; init; }

    public required IReadOnlyList<string> SeatNos { get; init; }

    public required int TotalAmount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One page of matching bookings, newest first, with how many match across all pages.</summary>
public sealed record AdminBookingPage
{
    public required IReadOnlyList<AdminBookingSummary> Items { get; init; }

    public required int Page { get; init; }

    public required int TotalItems { get; init; }
}

/// <summary>The criteria as typed: a box left empty is not a criterion.</summary>
public sealed record BookingCriteria(string BookingId, string Email, string TripId)
{
    public bool IsEmpty
    {
        get { return BookingId.Length == 0 && Email.Length == 0 && TripId.Length == 0; }
    }
}

/// <summary>What the page shows for a search: the bookings, or why there are none; field messages sit beside their boxes.</summary>
public sealed record BookingSearchView
{
    public AdminBookingPage? Results { get; init; }

    public PageAlert? Alert { get; init; }

    /// <summary>"Enter a booking ID, an email address or a trip ID." when all three boxes are empty.</summary>
    public string? CriteriaError { get; init; }

    public string? BookingIdError { get; init; }

    public string? EmailError { get; init; }

    public string? TripIdError { get; init; }
}

/// <summary>The body Booking's search takes; a criterion not given is null.</summary>
public sealed record BookingSearchRequest(Guid? BookingId, string? ContactEmail, Guid? TripId);

public sealed class BookingsHandler
{
    /// <summary>How many bookings one page shows.</summary>
    public const int PageSize = 20;

    public const string CriteriaMissing = "Enter a booking ID, an email address or a trip ID.";

    private const int MaxEmailLength = 254;

    private readonly IHttpClientFactory _httpClientFactory;

    public BookingsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Bookings that match every criterion given, in every status. A search with nothing typed, or
    /// with a criterion that is not an identifier or an address, is never sent.
    /// </summary>
    public async Task<BookingSearchView> SearchAsync(BookingCriteria criteria, int page, string correlationId, CancellationToken cancellationToken)
    {
        BookingSearchView? refused = Check(criteria);
        if (refused != null)
        {
            return refused;
        }

        string address = $"bookings/search?page={page.ToString(CultureInfo.InvariantCulture)}&pageSize={PageSize}";
        var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = JsonContent.Create(ToRequest(criteria), options: WireJson.Options),
        };
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        ApiResult<AdminBookingPage> api = await response.ReadApiResultAsync<AdminBookingPage>(cancellationToken);
        if (api.IsSuccess)
        {
            return new BookingSearchView { Results = api.Value };
        }

        if (api.Errors != null)
        {
            return ToFieldErrors(api.Errors);
        }

        return new BookingSearchView { Alert = ToAlert(api.Problem, correlationId) };
    }

    /// <summary>"16 bookings." and "1 booking.".</summary>
    public static string CountText(int total)
    {
        if (total == 1)
        {
            return "1 booking.";
        }

        return $"{total} bookings.";
    }

    /// <summary>How many pages the matching bookings fill.</summary>
    public static int PageCount(int total)
    {
        return (total + PageSize - 1) / PageSize;
    }

    // Null when every typed criterion may be sent.
    private static BookingSearchView? Check(BookingCriteria criteria)
    {
        if (criteria.IsEmpty)
        {
            return new BookingSearchView { CriteriaError = CriteriaMissing };
        }

        string? bookingIdError = IdError(criteria.BookingId, "booking ID");
        string? tripIdError = IdError(criteria.TripId, "trip ID");
        string? emailError = EmailError(criteria.Email);
        if (bookingIdError is null && tripIdError is null && emailError is null)
        {
            return null;
        }

        return new BookingSearchView { BookingIdError = bookingIdError, TripIdError = tripIdError, EmailError = emailError };
    }

    private static string? IdError(string text, string field)
    {
        if (text.Length == 0 || Guid.TryParse(text, out Guid _))
        {
            return null;
        }

        return Messages.ValidationFormat(field);
    }

    private static string? EmailError(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        bool isEmail = text.Length <= MaxEmailLength && new EmailAddressAttribute().IsValid(text);
        if (isEmail)
        {
            return null;
        }

        return Messages.ValidationFormat("email address");
    }

    private static BookingSearchRequest ToRequest(BookingCriteria criteria)
    {
        Guid? bookingId = ParseOrNull(criteria.BookingId);
        Guid? tripId = ParseOrNull(criteria.TripId);
        string? email = null;
        if (criteria.Email.Length > 0)
        {
            email = criteria.Email;
        }

        return new BookingSearchRequest(bookingId, email, tripId);
    }

    private static Guid? ParseOrNull(string text)
    {
        if (Guid.TryParse(text, out Guid id))
        {
            return id;
        }

        return null;
    }

    // Booking's own refusal names the box it did not like.
    private static BookingSearchView ToFieldErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        string? bookingIdError = FieldError(errors, "bookingId", "booking ID");
        string? emailError = FieldError(errors, "contactEmail", "email address");
        string? tripIdError = FieldError(errors, "tripId", "trip ID");
        bool isNamed = bookingIdError != null || emailError != null || tripIdError != null;
        if (!isNamed)
        {
            return new BookingSearchView { CriteriaError = CriteriaMissing };
        }

        return new BookingSearchView { BookingIdError = bookingIdError, EmailError = emailError, TripIdError = tripIdError };
    }

    private static string? FieldError(IReadOnlyDictionary<string, string[]> errors, string key, string field)
    {
        if (errors.ContainsKey(key))
        {
            return Messages.ValidationFormat(field);
        }

        return null;
    }

    // Booking answers only an admin; any refusal without words of its own is the shared one.
    private static PageAlert ToAlert(ApiProblem? problem, string correlationId)
    {
        if (problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(correlationId));
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }
}
