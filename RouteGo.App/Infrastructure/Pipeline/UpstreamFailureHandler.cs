using System.Net;
using System.Net.Http.Headers;

namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Gives every service call one time budget and turns an unreachable or silent service into the
/// same problem answer the services themselves send for an outage (UPSTREAM_UNAVAILABLE /
/// UPSTREAM_TIMEOUT), so a page shows "not available right now" and keeps what the person typed
/// instead of failing on the error page. Only reads (GET) are retried, once; a write is never
/// sent twice by the portal on its own.
/// </summary>
public sealed class UpstreamFailureHandler : DelegatingHandler
{
    // 10 s per attempt: long past a healthy Identity answer, short enough that a person waiting on
    // a page gets the "not available" message quickly. With one read retry the worst case is about
    // 20 s, inside HttpClient's own 100 s timeout.
    private static readonly TimeSpan _attemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _retryPause = TimeSpan.FromMilliseconds(200);
    private const int ReadAttempts = 2;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int attempts = 1;
        if (request.Method == HttpMethod.Get)
        {
            attempts = ReadAttempts;
        }

        for (int attempt = 1; attempt < attempts; attempt++)
        {
            HttpResponseMessage? response = await TrySendAsync(request, cancellationToken);
            if (response != null && !IsRetryable(response.StatusCode))
            {
                return response;
            }

            if (response != null)
            {
                response.Dispose();
            }
            await Task.Delay(_retryPause, cancellationToken);
        }

        return await SendLastAttemptAsync(request, cancellationToken);
    }

    // An earlier attempt: null means "failed in transit, try again".
    private async Task<HttpResponseMessage?> TrySendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_attemptTimeout);
        try
        {
            return await base.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendLastAttemptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_attemptTimeout);
        try
        {
            return await base.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OutageProblem(HttpStatusCode.GatewayTimeout, "UPSTREAM_TIMEOUT", request);
        }
        catch (HttpRequestException)
        {
            return OutageProblem(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", request);
        }
    }

    private static bool IsRetryable(HttpStatusCode status)
    {
        return status == HttpStatusCode.BadGateway
            || status == HttpStatusCode.ServiceUnavailable
            || status == HttpStatusCode.GatewayTimeout;
    }

    private static HttpResponseMessage OutageProblem(HttpStatusCode status, string code, HttpRequestMessage request)
    {
        var body = new OutageBody(code, "The service could not be reached.", (int)status);
        var mediaType = new MediaTypeHeaderValue("application/problem+json");
        var content = JsonContent.Create(body, mediaType: mediaType, options: WireJson.Options);
        return new HttpResponseMessage(status) { Content = content, RequestMessage = request };
    }

    // "unreachable" marks the answer as the portal's own: a service that is up but could not do its
    // job sends the same codes (Booking's UPSTREAM_UNAVAILABLE means Seat Inventory or Payment was
    // down), and a page words the two differently — see BookingProblems.IsUnreachable.
    private sealed record OutageBody(string Code, string Title, int Status)
    {
        public bool Unreachable { get; init; } = true;
    }
}
