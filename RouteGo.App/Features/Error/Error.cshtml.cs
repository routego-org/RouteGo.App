namespace RouteGo.App.Features.Error;

/// <summary>
/// The page shown for an address the portal does not have (status 404) and for a page that failed
/// unexpectedly (status 500). It is reached only by the host's own re-execution, never by a link.
/// </summary>
public sealed class ErrorModel : PageModel
{
    public bool IsNotFound { get; private set; }

    /// <summary>The request's correlation id, quoted so support can find the failure in the log.</summary>
    public string TraceId { get; private set; } = "";

    public void OnGet(int? statusCode)
    {
        IsNotFound = statusCode == StatusCodes.Status404NotFound;
        TraceId = CorrelationId.Get(HttpContext);
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (IsNotFound)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}
