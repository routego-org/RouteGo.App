namespace RouteGo.App.Common.UI;

/// <summary>
/// Marks an Identity request that must carry the signed-in person's access token. Public
/// operations (register, sign in, verify) are sent unmarked, so they never trigger a session
/// renewal on behalf of someone who happens to be signed in.
/// </summary>
public static class IdentityRequests
{
    private static readonly HttpRequestOptionsKey<bool> _userTokenKey =
        new HttpRequestOptionsKey<bool>("RouteGo.UserToken");

    private static readonly HttpRequestOptionsKey<bool> _refreshTokenBodyKey =
        new HttpRequestOptionsKey<bool>("RouteGo.RefreshTokenBody");

    /// <summary>
    /// Marks a sign-out request: besides the access token, its body carries the session's refresh
    /// token — the one current AFTER any renewal, which only IdentityTokenHandler knows.
    /// </summary>
    public static HttpRequestMessage WithSessionRefreshToken(HttpRequestMessage request)
    {
        request.Options.Set(_refreshTokenBodyKey, true);
        return WithUserToken(request);
    }

    /// <summary>Whether the request was marked with <see cref="WithSessionRefreshToken"/>.</summary>
    public static bool NeedsRefreshTokenBody(HttpRequestMessage request)
    {
        if (!request.Options.TryGetValue(_refreshTokenBodyKey, out bool needsBody))
        {
            return false;
        }

        return needsBody;
    }

    /// <summary>Marks the request so IdentityTokenHandler attaches (and if needed renews) the person's token.</summary>
    public static HttpRequestMessage WithUserToken(HttpRequestMessage request)
    {
        request.Options.Set(_userTokenKey, true);
        return request;
    }

    /// <summary>Whether the request was marked with <see cref="WithUserToken"/>.</summary>
    public static bool NeedsUserToken(HttpRequestMessage request)
    {
        if (!request.Options.TryGetValue(_userTokenKey, out bool needsToken))
        {
            return false;
        }

        return needsToken;
    }
}
