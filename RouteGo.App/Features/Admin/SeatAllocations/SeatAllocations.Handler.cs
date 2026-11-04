namespace RouteGo.App.Features.Admin.SeatAllocations;

/// <summary>One seat of a trip as Seat Inventory gives it to an admin: with who holds or owns it, and until when.</summary>
public sealed record StaffSeat
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

    /// <summary>The booking holding or owning a held or booked seat; null for any other.</summary>
    public string? BookingId { get; init; }

    /// <summary>When a held seat's hold ends; null for any other.</summary>
    public DateTimeOffset? HoldExpiresAt { get; init; }

    /// <summary>operator or tripCancelled for a blocked seat; null for any other.</summary>
    public string? BlockReason { get; init; }
}

/// <summary>Every seat of a trip, and whether the trip was cancelled or has left.</summary>
public sealed record StaffSeatList
{
    public required Guid TripId { get; init; }

    public required DateTimeOffset DepartureAt { get; init; }

    public required bool IsCancelled { get; init; }

    /// <summary>Whether the trip had left when Seat Inventory answered.</summary>
    public required bool IsDeparted { get; init; }

    public required IReadOnlyList<StaffSeat> Items { get; init; }
}

public sealed record HoldCancellation(string Note);

/// <summary>A cancelled hold: the seats it freed (or blocked, on a cancelled trip).</summary>
public sealed record CancelledHold(IReadOnlyList<string> ReleasedSeatNos);

public sealed class SeatAllocationsHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SeatAllocationsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Every seat of the trip with its booking, hold end time and block reason.</summary>
    public async Task<ApiResult<StaffSeatList>> ListSeatsAsync(Guid tripId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"trips/{tripId}/seatAllocations");
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<StaffSeatList>(cancellationToken);
    }

    /// <summary>Frees every seat the booking holds on the trip, with the admin's reason kept in Seat Inventory's audit record.</summary>
    public async Task<ApiResult<CancelledHold>> CancelHoldAsync(Guid tripId, Guid bookingId, string reason, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"trips/{tripId}/holds/{bookingId}/cancel");
        request.Content = JsonContent.Create(new HoldCancellation(reason), options: WireJson.Options);
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<CancelledHold>(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpClient seatInventory = _httpClientFactory.CreateClient(HttpClients.SeatInventory);
        return await seatInventory.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
    }
}
