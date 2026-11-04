using RouteGo.App.Infrastructure.Pipeline;

namespace RouteGo.App.Features.Auth.Logout;

public sealed class LogoutHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public LogoutHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Asks Identity to revoke this browser's refresh token chain. Sign-out goes ahead whatever
    /// Identity answers, so nothing is returned: a session that cannot be renewed, an outage, or a
    /// refusal all end the same way.
    /// </summary>
    public async Task RevokeAsync(CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new HttpRequestMessage(HttpMethod.Post, "auth/sessions/revoke");
        try
        {
            HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithSessionRefreshToken(request), cancellationToken);
            response.Dispose();
        }
        catch (SessionEndedException)
        {
            // The renewal was refused or Identity answered UNAUTHORIZED: nothing is left to revoke.
        }
        catch (HttpRequestException)
        {
            // A server error: the browser is signed out regardless.
        }
    }
}

public sealed class LogoutModel : PageModel
{
    private readonly LogoutHandler _handler;

    public LogoutModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new LogoutHandler(httpClientFactory);
    }

    // Opening the address only asks; signing out is a protected form submit.
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await _handler.RevokeAsync(cancellationToken);
        await PortalSession.SignOutAsync(HttpContext);
        TempData[Flash.Key] = Messages.SuccessSignedOut;
        return RedirectToPage("/Home/Home/Home");
    }
}
