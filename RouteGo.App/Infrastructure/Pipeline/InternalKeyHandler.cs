using Amazon.SecretsManager.Model;

namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Presents the RouteGo internal key on every call to Notification's internal operations — the only
/// thing Notification checks, because it cannot tell who the person is. The key is read from the
/// secret store on the server and never reaches a browser or a log line. A key that cannot be found
/// is not sent at all: Notification then answers UNAUTHORIZED and the page tells the admin the email
/// log is not set up correctly, instead of failing on the error page.
/// </summary>
public sealed class InternalKeyHandler : DelegatingHandler
{
    /// <summary>The secret holding the key, shared with Notification (one name in every environment).</summary>
    public const string SecretName = "routego/internal-key";

    /// <summary>The header Notification reads the key from.</summary>
    public const string HeaderName = "X-Internal-Key";

    private readonly SecretResolver _secrets;
    private readonly ILogger<InternalKeyHandler> _logger;

    public InternalKeyHandler(SecretResolver secrets, ILogger<InternalKeyHandler> logger)
    {
        _secrets = secrets;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? key = await ResolveKeyAsync();
        if (!string.IsNullOrEmpty(key))
        {
            request.Headers.Remove(HeaderName);
            request.Headers.Add(HeaderName, key);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    // Resolved on every call through the secret cache, so a rotated key is picked up without a restart.
    private async Task<string?> ResolveKeyAsync()
    {
        try
        {
            return await _secrets.ResolveAsync(SecretName);
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogWarning("Secret {SecretName} was not found; Notification is called without the internal key", SecretName);
            return null;
        }
    }
}
