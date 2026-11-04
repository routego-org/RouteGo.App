using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Renews sessions, at most one renewal per session at a time. A refresh token works only once:
/// two renewals with the same token make Identity treat it as stolen and end the session. So
/// concurrent requests of one session queue on a lock, and the ones that waited reuse the result
/// the first one got. The state is per process, which matches the single Portal instance; more
/// than one instance would need a shared store (an open question in the session spec).
/// </summary>
public sealed class SessionRenewal
{
    // Renew this long before expiry, so a token never expires between the check and the call.
    private static readonly TimeSpan _renewalMargin = TimeSpan.FromSeconds(60);

    // Long enough for requests that queued behind a renewal to pick up its result.
    private static readonly TimeSpan _recentResultLifetime = TimeSpan.FromMinutes(2);

    // A fixed set of locks shared by hashing the session id: no per-session lock to clean up,
    // at the cost of two sessions occasionally waiting for each other.
    private const int LockCount = 64;

    private readonly SemaphoreSlim[] _locks;
    private readonly ConcurrentDictionary<string, RecentRenewal> _recent;
    private readonly IClock _clock;

    private sealed record RecentRenewal(TokenPair Tokens, DateTimeOffset RenewedAt);

    public SessionRenewal(IClock clock)
    {
        _clock = clock;
        _recent = new ConcurrentDictionary<string, RecentRenewal>();
        _locks = new SemaphoreSlim[LockCount];
        for (int i = 0; i < LockCount; i++)
        {
            _locks[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>Whether the access token has expired or expires within the renewal margin.</summary>
    public bool NeedsRenewal(TokenPair tokens)
    {
        DateTimeOffset renewFrom = tokens.AccessTokenExpiresAt - _renewalMargin;
        return _clock.UtcNow >= renewFrom;
    }

    /// <summary>
    /// Exchanges the session's refresh token for a new pair, or reuses the pair a concurrent
    /// request of the same session just got. <paramref name="sendRefreshAsync"/> performs the
    /// refreshSession call.
    /// </summary>
    public async Task<RenewalResult> RenewAsync(
        string sessionId,
        TokenPair current,
        Func<string, CancellationToken, Task<HttpResponseMessage>> sendRefreshAsync,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _locks[LockIndex(sessionId)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            RemoveExpiredResults();
            string key = Fingerprint(current.RefreshToken);
            if (_recent.TryGetValue(key, out RecentRenewal? recent))
            {
                return new RenewalResult(RenewalOutcome.Renewed, recent.Tokens, null);
            }

            HttpResponseMessage response = await sendRefreshAsync(current.RefreshToken, cancellationToken);
            RenewalResult result = await ReadResultAsync(response, cancellationToken);
            if (result.Tokens != null)
            {
                _recent[key] = new RecentRenewal(result.Tokens, _clock.UtcNow);
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<RenewalResult> ReadResultAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            TokenPair? tokens = await response.Content.ReadFromJsonAsync<TokenPair>(WireJson.Options, cancellationToken);
            response.Dispose();
            return new RenewalResult(RenewalOutcome.Renewed, tokens, null);
        }

        // REFRESH_TOKEN_INVALID / REFRESH_TOKEN_REUSED (401), or VALIDATION_FAILED (400): the
        // session holds no usable refresh token, so it ends — Identity does not say which.
        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.BadRequest)
        {
            response.Dispose();
            return new RenewalResult(RenewalOutcome.Ended, null, null);
        }

        return new RenewalResult(RenewalOutcome.Failed, null, response);
    }

    private void RemoveExpiredResults()
    {
        DateTimeOffset oldest = _clock.UtcNow - _recentResultLifetime;
        foreach (KeyValuePair<string, RecentRenewal> entry in _recent)
        {
            if (entry.Value.RenewedAt < oldest)
            {
                _recent.TryRemove(entry.Key, out RecentRenewal? _);
            }
        }
    }

    private static int LockIndex(string sessionId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return hash[0] % LockCount;
    }

    // Keys the recent results by a hash, so the process never keeps refresh tokens as lookup keys.
    private static string Fingerprint(string refreshToken)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash);
    }
}
