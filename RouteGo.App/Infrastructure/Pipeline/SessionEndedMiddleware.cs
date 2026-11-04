namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Ends a session that can no longer be used, wherever in a page that was discovered: removes the
/// session cookie and sends the person to sign in, with the current page as the return address and
/// the "session ended" arrival message.
/// </summary>
public sealed class SessionEndedMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionEndedMiddleware> _logger;

    public SessionEndedMiddleware(RequestDelegate next, ILogger<SessionEndedMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (SessionEndedException exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            _logger.LogInformation("Session ended: {Reason}", exception.Message);
            await PortalSession.SignOutAsync(context);
            string returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            context.Response.Redirect(SignInAddress.Build(returnUrl, SignInAddress.ReasonSessionEnded));
        }
    }
}
