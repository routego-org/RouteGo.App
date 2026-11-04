namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Sends the current request's correlation id on every outbound service call, so one person's
/// action can be followed through the portal and the services in the logs.
/// </summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            request.Headers.Remove(CorrelationId.HeaderName);
            request.Headers.Add(CorrelationId.HeaderName, CorrelationId.Get(httpContext));
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
