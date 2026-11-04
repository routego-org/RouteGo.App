// Test-project helper: Identity answers for slice tests.
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// Identity's answers, built once for every slice test: a token pair whose access token carries
/// the claims Identity puts in it, and RFC 7807 problem answers with a code and extensions.
/// </summary>
public static class IdentityStubs
{
    /// <summary>A token pair for a person, with the access token expiring at the given moment.</summary>
    public static TokenPair Tokens(string email, string role, DateTimeOffset accessTokenExpiresAt)
    {
        string refreshToken = "refresh-" + Guid.NewGuid().ToString("N");
        return new TokenPair
        {
            AccessToken = AccessToken(email, role),
            AccessTokenExpiresAt = accessTokenExpiresAt,
            RefreshToken = refreshToken,
            // The session cookie expires with the refresh token, and the test browser drops a cookie
            // by the real clock — so it stays far in the future even when the access token is pinned
            // to FixedClock.Now.
            RefreshTokenExpiresAt = _farFuture,
            TokenType = "Bearer",
        };
    }

    // Far in the future, so a test that is not about renewal never triggers one.
    private static readonly DateTimeOffset _farFuture = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A token pair whose access token does not expire during any test.</summary>
    public static TokenPair Tokens(string email, string role)
    {
        return Tokens(email, role, _farFuture);
    }

    /// <summary>A 200 answer carrying the token pair.</summary>
    public static HttpResponseMessage TokenAnswer(TokenPair tokens)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(tokens, options: WireJson.Options),
        };
    }

    /// <summary>A problem+json answer with a code and optional extension members.</summary>
    public static HttpResponseMessage Problem(
        HttpStatusCode status, string code, Dictionary<string, object>? extensions = null)
    {
        var body = new Dictionary<string, object>
        {
            ["type"] = "urn:routego:identity:problems:" + code.ToLowerInvariant().Replace('_', '-'),
            ["title"] = "Problem " + code,
            ["status"] = (int)status,
            ["code"] = code,
            ["traceId"] = "trace-" + code.ToLowerInvariant(),
        };
        if (extensions != null)
        {
            foreach (KeyValuePair<string, object> extension in extensions)
            {
                body[extension.Key] = extension.Value;
            }
        }

        var mediaType = new MediaTypeHeaderValue("application/problem+json");
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body, mediaType) };
    }

    /// <summary>A 400 validation problem naming fields, the way Identity reports VALIDATION_FAILED.</summary>
    public static HttpResponseMessage ValidationProblem(string field, string message)
    {
        var errors = new Dictionary<string, string[]> { [field] = new[] { message } };
        var body = new Dictionary<string, object>
        {
            ["title"] = "One or more validation errors occurred.",
            ["status"] = 400,
            ["code"] = "VALIDATION_FAILED",
            ["errors"] = errors,
        };
        var mediaType = new MediaTypeHeaderValue("application/problem+json");
        return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(body, mediaType) };
    }

    private static string AccessToken(string email, string role)
    {
        string header = Encode(new Dictionary<string, object> { ["alg"] = "RS256", ["kid"] = "test-key" });
        var claims = new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["email"] = email,
            ["role"] = role,
            ["aud"] = "routego",
        };
        string payload = Encode(claims);
        return header + "." + payload + ".test-signature";
    }

    private static string Encode(Dictionary<string, object> values)
    {
        byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values));
        return Base64Url.EncodeToString(json);
    }
}
