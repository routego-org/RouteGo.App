using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The person's session: an encrypted, HttpOnly cookie holding who they are (the access token's
/// claims) and Identity's token pair. Every page reads it; sign-in, email verification and session
/// renewal write it, so starting and renewing a session happen in exactly one place.
/// </summary>
public static class PortalSession
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public const string SessionIdClaim = "sid";
    public const string UserIdClaim = "sub";
    public const string EmailClaim = "email";
    public const string OperatorIdClaim = "operatorId";
    public const string RoleClaim = ClaimTypes.Role;

    /// <summary>Identity's role claim values (the access token spells them in PascalCase).</summary>
    public const string RoleCustomer = "Customer";
    public const string RoleOperator = "Operator";
    public const string RoleAdmin = "Admin";

    private const string AccessTokenName = "access_token";
    private const string AccessTokenExpiresAtName = "access_token_expires_at";
    private const string RefreshTokenName = "refresh_token";
    private const string RefreshTokenExpiresAtName = "refresh_token_expires_at";
    private const int SessionIdBytes = 16;

    /// <summary>Starts a new session from a token pair, replacing any session already in this browser.</summary>
    public static async Task SignInAsync(HttpContext httpContext, TokenPair tokens)
    {
        string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(SessionIdBytes));
        await WriteAsync(httpContext, sessionId, tokens);
    }

    /// <summary>Stores a renewed token pair in the same session (same session id).</summary>
    public static async Task RenewAsync(HttpContext httpContext, string sessionId, TokenPair tokens)
    {
        await WriteAsync(httpContext, sessionId, tokens);
    }

    /// <summary>Removes the session cookie from this browser.</summary>
    public static async Task SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(Scheme);
    }

    /// <summary>The token pair held by the session, or null when the session carries none.</summary>
    public static TokenPair? ReadTokens(AuthenticationProperties properties)
    {
        string? accessToken = properties.GetTokenValue(AccessTokenName);
        string? accessExpiresAt = properties.GetTokenValue(AccessTokenExpiresAtName);
        string? refreshToken = properties.GetTokenValue(RefreshTokenName);
        string? refreshExpiresAt = properties.GetTokenValue(RefreshTokenExpiresAtName);
        if (accessToken is null || accessExpiresAt is null || refreshToken is null || refreshExpiresAt is null)
        {
            return null;
        }

        return new TokenPair
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = DateTimeOffset.Parse(accessExpiresAt, CultureInfo.InvariantCulture),
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = DateTimeOffset.Parse(refreshExpiresAt, CultureInfo.InvariantCulture),
            TokenType = "Bearer",
        };
    }

    private static async Task WriteAsync(HttpContext httpContext, string sessionId, TokenPair tokens)
    {
        ClaimsPrincipal principal = CreatePrincipal(sessionId, ReadClaims(tokens.AccessToken));
        var properties = new AuthenticationProperties
        {
            // The session lasts as long as the refresh token can renew it, across browser restarts.
            IsPersistent = true,
            ExpiresUtc = tokens.RefreshTokenExpiresAt,
            AllowRefresh = false,
        };
        properties.StoreTokens(CreateTokenList(tokens));
        await httpContext.SignInAsync(Scheme, principal, properties);
    }

    // Only the payload is read: the token arrives straight from Identity over the private
    // network, and every service that receives it validates the signature itself.
    private static AccessTokenClaims ReadClaims(string accessToken)
    {
        string[] parts = accessToken.Split('.');
        if (parts.Length != 3)
        {
            throw new FormatException("The access token is not a JWT (expected three dot-separated parts).");
        }

        byte[] payloadBytes = Base64Url.DecodeFromChars(parts[1]);
        AccessTokenClaims? claims = JsonSerializer.Deserialize<AccessTokenClaims>(payloadBytes, WireJson.Options);
        if (claims is null || claims.Sub is null || claims.Email is null || claims.Role is null)
        {
            throw new FormatException("The access token payload lacks one of sub, email or role.");
        }

        return claims;
    }

    private static ClaimsPrincipal CreatePrincipal(string sessionId, AccessTokenClaims claims)
    {
        var identity = new ClaimsIdentity(Scheme, EmailClaim, ClaimTypes.Role);
        identity.AddClaim(new Claim(SessionIdClaim, sessionId));
        identity.AddClaim(new Claim(UserIdClaim, claims.Sub!));
        identity.AddClaim(new Claim(EmailClaim, claims.Email!));
        identity.AddClaim(new Claim(ClaimTypes.Role, claims.Role!));
        if (claims.OperatorId != null)
        {
            identity.AddClaim(new Claim(OperatorIdClaim, claims.OperatorId));
        }

        return new ClaimsPrincipal(identity);
    }

    private static List<AuthenticationToken> CreateTokenList(TokenPair tokens)
    {
        var list = new List<AuthenticationToken>();
        list.Add(new AuthenticationToken { Name = AccessTokenName, Value = tokens.AccessToken });
        list.Add(new AuthenticationToken { Name = AccessTokenExpiresAtName, Value = tokens.AccessTokenExpiresAt.ToString("O") });
        list.Add(new AuthenticationToken { Name = RefreshTokenName, Value = tokens.RefreshToken });
        list.Add(new AuthenticationToken { Name = RefreshTokenExpiresAtName, Value = tokens.RefreshTokenExpiresAt.ToString("O") });
        return list;
    }

    // The access token's claims: sub, email, role, and operatorId for operators.
    private sealed record AccessTokenClaims(string? Sub, string? Email, string? Role)
    {
        public string? OperatorId { get; init; }
    }
}
