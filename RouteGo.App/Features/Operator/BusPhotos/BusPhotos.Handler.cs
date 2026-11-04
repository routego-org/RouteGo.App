namespace RouteGo.App.Features.Operator.BusPhotos;

/// <summary>One uploaded photo of a bus. Only what the page needs is read.</summary>
public sealed record BusPhotoItem(Guid PhotoId, string? Url);

/// <summary>The bus as the photos page needs it: its name and its uploaded photos in order.</summary>
public sealed record PhotosBus(Guid BusId, string Name, IReadOnlyList<BusPhotoItem> Photos);

/// <summary>Asks Fleet for a place to send one file of the format and size.</summary>
public sealed record PhotoUploadRequest(string Format, long SizeBytes);

/// <summary>Where the browser sends one file, and how. Valid for a few minutes.</summary>
public sealed record PhotoUploadSlot
{
    public required Guid PhotoId { get; init; }

    public required string UploadUrl { get; init; }

    /// <summary>The upload's method as Fleet spells it ("put").</summary>
    public required string UploadMethod { get; init; }

    /// <summary>The content type the upload must declare: image/jpeg or image/png.</summary>
    public required string ContentType { get; init; }
}

/// <summary>Every uploaded photo of the bus, exactly once, in the new order.</summary>
public sealed record PhotoOrderRequest(IReadOnlyList<Guid> PhotoIds);

/// <summary>The bus's photos in their new order.</summary>
public sealed record PhotoList(IReadOnlyList<BusPhotoItem> Items);

/// <summary>
/// One photo moved within the order the page showed. The page's order is used, not a fresh one,
/// so Fleet can tell when the photos changed meanwhile.
/// </summary>
/// <param name="ShownOrder">Every photo, in the order the page showed them.</param>
/// <param name="PhotoId">The photo that moves.</param>
/// <param name="Move">One of <see cref="PhotoMove"/>'s values.</param>
public sealed record PhotoMoveRequest(IReadOnlyList<Guid> ShownOrder, Guid PhotoId, string Move);

/// <summary>How one photo moves in the order.</summary>
public static class PhotoMove
{
    public const string Earlier = "earlier";
    public const string Later = "later";
    public const string Cover = "cover";
}

public sealed class BusPhotosHandler
{
    private const string BusesPath = "buses";

    private readonly IHttpClientFactory _httpClientFactory;

    public BusPhotosHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The operator's bus with its uploaded photos in order.</summary>
    public async Task<ApiResult<PhotosBus>> GetBusAsync(Guid busId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BusesPath}/{busId}");
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<PhotosBus>(cancellationToken);
    }

    /// <summary>Asks Fleet for an upload slot: a pending photo and the address its file goes to.</summary>
    public async Task<ApiResult<PhotoUploadSlot>> CreatePhotoAsync(Guid busId, PhotoUploadRequest photo, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BusesPath}/{busId}/photos")
        {
            Content = JsonContent.Create(photo, options: WireJson.Options),
        };
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<PhotoUploadSlot>(cancellationToken);
    }

    /// <summary>Asks Fleet to check the stored file and add the photo last in the order.</summary>
    public async Task<ApiResult<BusPhotoItem>> ConfirmPhotoAsync(Guid busId, Guid photoId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BusesPath}/{busId}/photos/{photoId}/confirm");
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<BusPhotoItem>(cancellationToken);
    }

    /// <summary>Removes a photo, or a pending photo whose file never arrived, so its place is free.</summary>
    public async Task<ApiResult<bool>> DeletePhotoAsync(Guid busId, Guid photoId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{BusesPath}/{busId}/photos/{photoId}");
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            // Fleet answers 204 with no body, so there is nothing to read.
            return ApiResult<bool>.Success(true);
        }

        return await response.ReadApiResultAsync<bool>(cancellationToken);
    }

    /// <summary>Moves one photo within the order the page showed and sends the whole new order.</summary>
    public async Task<ApiResult<PhotoList>> MovePhotoAsync(Guid busId, PhotoMoveRequest move, CancellationToken cancellationToken)
    {
        List<Guid> order = NewOrder(move.ShownOrder, move.PhotoId, move.Move);
        var request = new HttpRequestMessage(HttpMethod.Put, $"{BusesPath}/{busId}/photoOrder")
        {
            Content = JsonContent.Create(new PhotoOrderRequest(order), options: WireJson.Options),
        };
        HttpResponseMessage response = await SendAsync(request, cancellationToken);
        return await response.ReadApiResultAsync<PhotoList>(cancellationToken);
    }

    private static List<Guid> NewOrder(IReadOnlyList<Guid> shownOrder, Guid photoId, string move)
    {
        var order = new List<Guid>(shownOrder);
        int from = order.IndexOf(photoId);
        if (from < 0)
        {
            // A photo the page did not show: the order goes as it was, and Fleet says what changed.
            return order;
        }

        int to = from;
        if (move == PhotoMove.Cover)
        {
            to = 0;
        }
        else if (move == PhotoMove.Earlier)
        {
            to = Math.Max(from - 1, 0);
        }
        else if (move == PhotoMove.Later)
        {
            to = Math.Min(from + 1, order.Count - 1);
        }

        order.RemoveAt(from);
        order.Insert(to, photoId);
        return order;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        return await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
    }
}
