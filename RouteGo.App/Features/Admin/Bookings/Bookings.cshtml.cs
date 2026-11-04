using System.Globalization;

namespace RouteGo.App.Features.Admin.Bookings;

public sealed class BookingsModel : PageModel
{
    private const string BookingIdKey = "bookingId";
    private const string EmailKey = "email";
    private const string TripIdKey = "tripId";
    private const string PageKey = "page";
    private const string BookingsAddress = "/admin/bookings";

    private readonly BookingsHandler _handler;

    public BookingsModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new BookingsHandler(httpClientFactory);
    }

    public BookingCriteria Criteria { get; private set; } = new BookingCriteria(string.Empty, string.Empty, string.Empty);

    public int PageNumber { get; private set; } = 1;

    /// <summary>Whether the address names a search at all; without one only the form is shown.</summary>
    public bool HasSearched { get; private set; }

    public BookingSearchView View { get; private set; } = new BookingSearchView();

    /// <summary>
    /// Searches when the address carries criteria. A form sends its empty boxes too, so an address
    /// with an empty criterion is sent on to the same search without it, and a link or a reload
    /// shows the same list.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        ReadAddress();
        if (!HasSearched)
        {
            return Page();
        }

        if (!Criteria.IsEmpty && HasEmptyCriterion())
        {
            return Redirect(SearchAddress(Criteria, PageNumber));
        }

        View = await _handler.SearchAsync(Criteria, PageNumber, CorrelationId.Get(HttpContext), cancellationToken);
        return Page();
    }

    /// <summary>The search at a page number; page 1 carries no page.</summary>
    public string PageAddress(int page)
    {
        return SearchAddress(Criteria, page);
    }

    /// <summary>A booking's own page, remembering this search so "Back to search" returns to it.</summary>
    public string BookingAddress(AdminBookingSummary booking)
    {
        string search = SearchAddress(Criteria, PageNumber);
        int start = search.IndexOf('?', StringComparison.Ordinal);
        string address = $"{BookingsAddress}/{booking.BookingId}";
        if (start < 0)
        {
            return address;
        }

        return $"{address}?back={Uri.EscapeDataString(search[(start + 1)..])}";
    }

    /// <summary>The search address for criteria and a page: only the boxes with something in them, written as the product's address shows them (an email's @ as %40).</summary>
    public static string SearchAddress(BookingCriteria criteria, int page)
    {
        var pairs = new List<string>();
        AddWhenTyped(pairs, BookingIdKey, criteria.BookingId);
        AddWhenTyped(pairs, EmailKey, criteria.Email);
        AddWhenTyped(pairs, TripIdKey, criteria.TripId);
        if (page > 1)
        {
            pairs.Add($"{PageKey}={page.ToString(CultureInfo.InvariantCulture)}");
        }

        if (pairs.Count == 0)
        {
            return BookingsAddress;
        }

        return $"{BookingsAddress}?{string.Join('&', pairs)}";
    }

    private static void AddWhenTyped(List<string> pairs, string key, string value)
    {
        if (value.Length > 0)
        {
            pairs.Add($"{key}={Uri.EscapeDataString(value)}");
        }
    }

    private bool HasEmptyCriterion()
    {
        IQueryCollection query = Request.Query;
        bool hasEmptyId = query.ContainsKey(BookingIdKey) && Criteria.BookingId.Length == 0;
        bool hasEmptyEmail = query.ContainsKey(EmailKey) && Criteria.Email.Length == 0;
        bool hasEmptyTrip = query.ContainsKey(TripIdKey) && Criteria.TripId.Length == 0;
        return hasEmptyId || hasEmptyEmail || hasEmptyTrip;
    }

    // Razor Pages reserves the name "page" for its own routing, so the address's page number is read by hand.
    private void ReadAddress()
    {
        IQueryCollection query = Request.Query;
        Criteria = new BookingCriteria(Text(query, BookingIdKey), Text(query, EmailKey), Text(query, TripIdKey));
        HasSearched = query.ContainsKey(BookingIdKey) || query.ContainsKey(EmailKey) || query.ContainsKey(TripIdKey);
        bool isPage = int.TryParse(Text(query, PageKey), NumberStyles.None, CultureInfo.InvariantCulture, out int page);
        if (isPage && page >= 1)
        {
            PageNumber = page;
        }
    }

    private static string Text(IQueryCollection query, string key)
    {
        string? typed = query[key];
        return (typed ?? string.Empty).Trim();
    }
}
