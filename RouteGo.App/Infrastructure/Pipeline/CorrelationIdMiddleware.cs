namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Establishes the request's correlation id, returns it in the response header, and puts it on
/// every log line written while the request runs.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = CorrelationId.Establish(context);
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;
        var scope = new Dictionary<string, object> { ["CorrelationId"] = correlationId };
        using (_logger.BeginScope(scope))
        {
            await _next(context);
        }
    }
}
