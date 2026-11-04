namespace RouteGo.App.Common.UI;

/// <summary>
/// The request's correlation id: taken from the incoming X-Correlation-Id header when it is a
/// sensible value, otherwise generated. It is written on every log line, sent on every service
/// call, and quoted to support when a page shows the internal-error message.
/// </summary>
public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";

    private const string ItemKey = "RouteGo.CorrelationId";
    private const int MaxLength = 64;

    /// <summary>The correlation id of the current request (set by CorrelationIdMiddleware).</summary>
    public static string Get(HttpContext httpContext)
    {
        if (httpContext.Items[ItemKey] is string correlationId)
        {
            return correlationId;
        }

        return httpContext.TraceIdentifier;
    }

    /// <summary>Stores the request's correlation id: the caller's, or a new one.</summary>
    public static string Establish(HttpContext httpContext)
    {
        string correlationId = Guid.NewGuid().ToString();
        string incoming = httpContext.Request.Headers[HeaderName].ToString();
        if (IsAcceptable(incoming))
        {
            correlationId = incoming;
        }

        httpContext.Items[ItemKey] = correlationId;
        return correlationId;
    }

    // Only short, plain identifiers are accepted, so a caller cannot inject text into log lines.
    private static bool IsAcceptable(string value)
    {
        if (value.Length == 0 || value.Length > MaxLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
