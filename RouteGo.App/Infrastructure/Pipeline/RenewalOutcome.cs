namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>How a renewal attempt ended.</summary>
public enum RenewalOutcome
{
    /// <summary>A new token pair is ready to use.</summary>
    Renewed,

    /// <summary>Identity refused the refresh token: the session is over.</summary>
    Ended,

    /// <summary>Identity could not be reached or failed; the session is kept and the page shows the answer.</summary>
    Failed,
}
