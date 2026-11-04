using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Puts the signed-in person's Identity access token on the requests that need it — to Identity and
/// to Fleet and Seat Inventory, which accept the same token — renewing the session first when the token has expired
/// or is about to. A refused renewal, or UNAUTHORIZED from a call made with a token, ends the
/// session (SessionEndedException). An Identity outage during renewal keeps the session and hands
/// the outage answer to the page. Holds no per-request state: the pooled handler chain is shared by
/// every request and every person.
/// </summary>
public sealed class IdentityTokenHandler : DelegatingHandler
{
    private const string RefreshPath = "auth/sessions/refresh";
    private const string RenewedTokensItem = "RouteGo.RenewedTokens";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SessionRenewal _renewal;
    private readonly Uri _identityBaseAddress;

    public IdentityTokenHandler(
        IHttpContextAccessor httpContextAccessor, SessionRenewal renewal, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _renewal = renewal;
        _identityBaseAddress = configuration.RequireBaseAddress(ConfigKeys.IdentityUrl);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IdentityRequests.NeedsUserToken(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        HttpContext httpContext = GetHttpContext();
        TokenPair tokens = await GetSessionTokensAsync(httpContext);
        if (_renewal.NeedsRenewal(tokens))
        {
            RenewalResult renewal = await RenewAsync(httpContext, tokens, cancellationToken);
            if (renewal.Outcome == RenewalOutcome.Failed)
            {
                return renewal.Response!;
            }

            tokens = renewal.Tokens!;
        }

        Authorize(request, tokens);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new SessionEndedException("A service answered UNAUTHORIZED to a call made with the session's token.");
        }

        return response;
    }

    private async Task<RenewalResult> RenewAsync(
        HttpContext httpContext, TokenPair tokens, CancellationToken cancellationToken)
    {
        string sessionId = httpContext.User.FindFirst(PortalSession.SessionIdClaim)!.Value;
        RenewalResult renewal = await _renewal.RenewAsync(sessionId, tokens, SendRefreshAsync, cancellationToken);
        if (renewal.Outcome == RenewalOutcome.Ended)
        {
            throw new SessionEndedException("Identity refused to renew the session.");
        }

        if (renewal.Outcome == RenewalOutcome.Renewed)
        {
            await PortalSession.RenewAsync(httpContext, sessionId, renewal.Tokens!);
            httpContext.Items[RenewedTokensItem] = renewal.Tokens;
        }

        return renewal;
    }

    private static void Authorize(HttpRequestMessage request, TokenPair tokens)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        if (IdentityRequests.NeedsRefreshTokenBody(request))
        {
            request.Content = JsonContent.Create(new RefreshTokenRequest(tokens.RefreshToken), options: WireJson.Options);
        }
    }

    // Sent through the rest of the chain only (base.SendAsync), so the refresh call is never itself
    // treated as a call that needs a token. The address is Identity's even on the Fleet client.
    private async Task<HttpResponseMessage> SendRefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var refresh = new HttpRequestMessage(HttpMethod.Post, new Uri(_identityBaseAddress, RefreshPath))
        {
            Content = JsonContent.Create(new RefreshTokenRequest(refreshToken), options: WireJson.Options),
        };
        return await base.SendAsync(refresh, cancellationToken);
    }

    private HttpContext GetHttpContext()
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            throw new InvalidOperationException("A service call with the person's token was made outside a request.");
        }

        return httpContext;
    }

    // A renewal earlier in this request wins over the cookie the browser sent, which still holds
    // the old pair.
    private static async Task<TokenPair> GetSessionTokensAsync(HttpContext httpContext)
    {
        if (httpContext.Items[RenewedTokensItem] is TokenPair renewed)
        {
            return renewed;
        }

        AuthenticateResult session = await httpContext.AuthenticateAsync(PortalSession.Scheme);
        TokenPair? tokens = null;
        if (session.Succeeded)
        {
            tokens = PortalSession.ReadTokens(session.Properties);
        }

        if (tokens is null)
        {
            throw new SessionEndedException("A page made a service call that needs a session, but there is none.");
        }

        return tokens;
    }

    private sealed record RefreshTokenRequest(string RefreshToken);
}
