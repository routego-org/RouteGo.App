namespace RouteGo.App.Features.Admin.SigningKeys;

/// <summary>
/// One public key as Identity publishes it. The key numbers (n, e) are left out: the page never
/// shows them, so it does not read them.
/// </summary>
public sealed record SigningKey
{
    public required string Kid { get; init; }

    public required string Kty { get; init; }

    public required string Alg { get; init; }

    public required string Use { get; init; }
}

/// <summary>Identity's public key set, in the order Identity returns it.</summary>
public sealed record SigningKeySet(IReadOnlyList<SigningKey> Keys);

public sealed class SigningKeysHandler
{
    // Public by design in Identity, so it is fetched without the admin's token; only the portal
    // limits this page to admins.
    private const string SigningKeysPath = ".well-known/jwks.json";

    private readonly IHttpClientFactory _httpClientFactory;

    public SigningKeysHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The key set Identity publishes right now; the page keeps no copy.</summary>
    public async Task<ApiResult<SigningKeySet>> GetSigningKeysAsync(CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        HttpResponseMessage response = await identity.GetAsync(SigningKeysPath, cancellationToken);
        return await response.ReadApiResultAsync<SigningKeySet>(cancellationToken);
    }
}

public sealed class SigningKeysModel : PageModel
{
    private readonly SigningKeysHandler _handler;

    public SigningKeysModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new SigningKeysHandler(httpClientFactory);
    }

    /// <summary>The published keys; null when Identity could not be asked.</summary>
    public IReadOnlyList<SigningKey>? Keys { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<SigningKeySet> api = await _handler.GetSigningKeysAsync(cancellationToken);
        if (api.IsSuccess)
        {
            Keys = api.Value!.Keys;
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }
}
