using System.Globalization;

namespace RouteGo.App.Features.Bookings.MyBookings;

/// <summary>One line of the customer's list: the trip, the seats, the money and how it ended.</summary>
public sealed record BookingListItem
{
    public required Guid BookingId { get; init; }

    public required string Status { get; init; }

    public required BookingTrip Trip { get; init; }

    public required IReadOnlyList<string> SeatNos { get; init; }

    public required int TotalAmount { get; init; }

    public BookingRefundInfo? Refund { get; init; }
}

/// <summary>One page of the customer's bookings, newest first, with how many there are in all.</summary>
public sealed record BookingListPage
{
    public required IReadOnlyList<BookingListItem> Items { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required int TotalItems { get; init; }
}

public sealed class MyBookingsHandler
{
    /// <summary>How many bookings one page lists.</summary>
    public const int PageSize = 20;

    private readonly IHttpClientFactory _httpClientFactory;

    public MyBookingsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The page number the address asks for: a whole number of 1 or more, otherwise page 1. A
    /// number too large to read is page 1 as well, so nothing typed is ever sent as it stands.
    /// </summary>
    public static int PageNumber(string? text)
    {
        bool isNumber = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int pageNumber);
        if (!isNumber || pageNumber < 1)
        {
            return 1;
        }

        return pageNumber;
    }

    /// <summary>How many pages the bookings fill.</summary>
    public static int PageCount(BookingListPage list)
    {
        return (list.TotalItems + PageSize - 1) / PageSize;
    }

    /// <summary>The signed-in customer's bookings that are not waiting for payment, newest first.</summary>
    public async Task<ApiResult<BookingListPage>> ListAsync(int pageNumber, CancellationToken cancellationToken)
    {
        HttpClient booking = _httpClientFactory.CreateClient(HttpClients.Booking);
        var request = new HttpRequestMessage(HttpMethod.Get, $"bookings?page={pageNumber}&pageSize={PageSize}");
        HttpResponseMessage response = await booking.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<BookingListPage>(cancellationToken);
    }
}
