using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Operator.SeatBlocking;

/// <summary>
/// One seat of the operator's trip, as Seat Inventory gives it to the trip's operator. Booking IDs
/// and hold end times come too; this page never shows them, so they are not read.
/// </summary>
public sealed record TripSeat
{
    public required string SeatNo { get; init; }

    /// <summary>lower or upper.</summary>
    public required string Deck { get; init; }

    public required int Row { get; init; }

    public required int Column { get; init; }

    public required string SeatClass { get; init; }

    /// <summary>Today's fare in whole rupees.</summary>
    public required int Fare { get; init; }

    /// <summary>available, held, booked or blocked.</summary>
    public required string Status { get; init; }

    /// <summary>operator or tripCancelled for a blocked seat; null for any other.</summary>
    public string? BlockReason { get; init; }
}

/// <summary>Every seat of one of the operator's trips, and whether the trip was cancelled or has left.</summary>
public sealed record TripSeatList
{
    public required Guid TripId { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    public required bool IsCancelled { get; init; }

    /// <summary>Whether the trip had left when Seat Inventory answered.</summary>
    public required bool IsDeparted { get; init; }

    public required IReadOnlyList<TripSeat> Items { get; init; }
}

public sealed record SeatBlockRequest(IReadOnlyList<string> SeatNos);

/// <summary>A block Seat Inventory accepted: every seat named, and the ones this request newly blocked (none when nothing changed).</summary>
public sealed record SeatBlock(IReadOnlyList<string> SeatNos, IReadOnlyList<string> BlockedSeatNos);

public sealed class SeatBlockingHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SeatBlockingHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Every seat of the trip with its status and, for a blocked seat, why.</summary>
    public async Task<ApiResult<TripSeatList>> ListSeatsAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"trips/{tripId}/seatAllocations");
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<TripSeatList>(cancellationToken);
    }

    /// <summary>Keeps the seats off sale, all of them or none.</summary>
    public async Task<ApiResult<SeatBlock>> BlockSeatsAsync(Guid tripId, IReadOnlyList<string> seatNos, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"trips/{tripId}/seatBlocks");
        request.Content = JsonContent.Create(new SeatBlockRequest(seatNos), options: WireJson.Options);
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<SeatBlock>(cancellationToken);
    }

    /// <summary>Puts seats the operator blocked back on sale, all of them or none.</summary>
    public async Task<ApiResult<bool>> UnblockSeatsAsync(Guid tripId, IReadOnlyList<string> seatNos, CancellationToken cancellationToken)
    {
        // Seat Inventory takes the seat numbers as one comma-separated value: seatNos=L1,L2.
        string address = QueryHelpers.AddQueryString($"trips/{tripId}/seatBlocks", "seatNos", string.Join(',', seatNos));
        var request = new HttpRequestMessage(HttpMethod.Delete, address);
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            // Seat Inventory answers 204 with no body, so there is nothing to read.
            return ApiResult<bool>.Success(true);
        }

        return await response.ReadApiResultAsync<bool>(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpClient seatInventory = _httpClientFactory.CreateClient(HttpClients.SeatInventory);
        return await seatInventory.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
    }
}
