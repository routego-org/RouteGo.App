using System.Globalization;

namespace RouteGo.App.Features.Buses.PublicBus;

/// <summary>One of the bus's photos, in Fleet's order (the first is the cover photo).</summary>
public sealed record PublicBusPhoto(Guid PhotoId, int Position, string Url);

/// <summary>What anyone may see of a bus, as Fleet gives it.</summary>
public sealed record PublicBusDetails
{
    public required Guid BusId { get; init; }

    public required string Name { get; init; }

    /// <summary>seater, sleeper or semiSleeper.</summary>
    public required string BusType { get; init; }

    public required bool IsAc { get; init; }

    public required IReadOnlyList<string> Amenities { get; init; }

    public required IReadOnlyList<string> SeatClasses { get; init; }

    public required int TotalSeats { get; init; }

    /// <summary>The bus company's name.</summary>
    public required string OperatorName { get; init; }

    /// <summary>0 to 5.</summary>
    public required decimal OperatorRating { get; init; }

    public required IReadOnlyList<PublicBusPhoto> Photos { get; init; }
}

public sealed class PublicBusHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PublicBusHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The bus's public details. Sent without the person's access token: the page is open to everyone
    /// and Fleet's public operation needs none.
    /// </summary>
    public async Task<ApiResult<PublicBusDetails>> GetPublicBusAsync(Guid busId, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        HttpResponseMessage response = await fleet.GetAsync($"public/buses/{busId}", cancellationToken);
        return await response.ReadApiResultAsync<PublicBusDetails>(cancellationToken);
    }
}

public sealed class PublicBusModel : PageModel
{
    private const string Entity = "bus";
    private const string HomeAddress = "/";

    private readonly PublicBusHandler _handler;

    public PublicBusModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new PublicBusHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public Guid BusId { get; set; }

    /// <summary>The loaded bus; null when it could not be loaded.</summary>
    public PublicBusDetails? Bus { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<PublicBusDetails> api = await _handler.GetPublicBusAsync(BusId, cancellationToken);
        if (api.IsSuccess)
        {
            Bus = api.Value;
            return;
        }

        PageAlert alert = FleetDisplay.ToAlert(api.Problem!, Entity, CorrelationId.Get(HttpContext));
        if (api.Problem!.Code == FleetDisplay.NotFound)
        {
            alert = alert.WithLink(Messages.AccessGoHome, HomeAddress);
        }

        Alert = alert;
    }

    /// <summary>The photos that can be shown, in Fleet's order; a photo whose address is not a web address is left out.</summary>
    public List<string> PhotoAddresses()
    {
        var addresses = new List<string>();
        foreach (PublicBusPhoto photo in Bus!.Photos)
        {
            string? address = FleetDisplay.SafePhotoAddress(photo.Url);
            if (address != null)
            {
                addresses.Add(address);
            }
        }

        return addresses;
    }

    /// <summary>"Rating 4.0 out of 5", always with one decimal place.</summary>
    public string RatingText()
    {
        string rating = Bus!.OperatorRating.ToString("0.0", CultureInfo.InvariantCulture);
        return $"Rating {rating} out of 5";
    }
}
