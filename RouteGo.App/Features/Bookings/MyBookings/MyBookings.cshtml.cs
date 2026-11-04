namespace RouteGo.App.Features.Bookings.MyBookings;

public sealed class MyBookingsModel : PageModel
{
    private const string PageAddress = "/my-bookings";

    private readonly MyBookingsHandler _handler;

    public MyBookingsModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new MyBookingsHandler(httpClientFactory);
    }

    /// <summary>The loaded page of bookings; null when they could not be loaded.</summary>
    public BookingListPage? List { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Razor Pages reserves the name "page" for its own routing, so the address's page number is read by hand.
        int pageNumber = MyBookingsHandler.PageNumber(Request.Query["page"]);
        ApiResult<BookingListPage> api = await _handler.ListAsync(pageNumber, cancellationToken);
        if (api.IsSuccess)
        {
            List = api.Value;
            return;
        }

        Alert = BookingProblems.ToLoadAlert(api, CorrelationId.Get(HttpContext));
    }

    /// <summary>"45 bookings", or "1 booking".</summary>
    public string TotalText()
    {
        if (List!.TotalItems == 1)
        {
            return "1 booking";
        }

        return $"{List.TotalItems} bookings";
    }

    public int PageCount()
    {
        return MyBookingsHandler.PageCount(List!);
    }

    /// <summary>The list at a page number; page 1 is the bare address.</summary>
    public string ListAddress(int pageNumber)
    {
        if (pageNumber <= 1)
        {
            return PageAddress;
        }

        return $"{PageAddress}?page={pageNumber}";
    }
}
