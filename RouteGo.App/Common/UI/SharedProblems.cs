namespace RouteGo.App.Common.UI;

/// <summary>
/// The portal-wide answer to a refusal a page has no specific words for (the product's shared
/// rules for every page): an outage shows the "not available" message, FORBIDDEN the access
/// message, and anything else the internal-error message with a reference support can quote.
/// A page switches on the codes its own spec names first, then falls back to this.
/// </summary>
public static class SharedProblems
{
    public const string UpstreamUnavailable = "UPSTREAM_UNAVAILABLE";
    public const string UpstreamTimeout = "UPSTREAM_TIMEOUT";
    public const string Forbidden = "FORBIDDEN";

    /// <summary>The page-level alert for a problem the page has no specific rule for.</summary>
    /// <param name="problem">The refusal the service answered.</param>
    /// <param name="correlationId">The request's correlation id, quoted when the problem has no trace id.</param>
    public static PageAlert ToAlert(ApiProblem problem, string correlationId)
    {
        if (problem.Code == UpstreamUnavailable || problem.Code == UpstreamTimeout)
        {
            return PageAlert.Danger(Messages.ProblemUpstream);
        }

        if (problem.Code == Forbidden)
        {
            return PageAlert.Danger(Messages.ProblemForbidden);
        }

        string reference = correlationId;
        if (!string.IsNullOrEmpty(problem.TraceId))
        {
            reference = problem.TraceId;
        }

        return PageAlert.Danger(Messages.ProblemInternalError(reference));
    }
}
