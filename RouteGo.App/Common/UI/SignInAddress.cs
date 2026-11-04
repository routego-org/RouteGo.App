using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The sign-in page's address and the query values other parts of the portal send to it: where to
/// return afterwards, and why the person was sent (so the page shows one arrival message).
/// </summary>
public static class SignInAddress
{
    /// <summary>The sign-in page; Notification also writes this path into the operator-approved email.</summary>
    public const string Path = "/auth/login";

    public const string ReturnUrlParameter = "returnUrl";
    public const string ReasonParameter = "reason";

    /// <summary>Sent by page access: the page needs sign-in.</summary>
    public const string ReasonSignInRequired = "sign-in-required";

    /// <summary>Sent because the session could not continue.</summary>
    public const string ReasonSessionEnded = "session-ended";

    /// <summary>The sign-in address carrying a return address and an arrival reason.</summary>
    public static string Build(string returnUrl, string reason)
    {
        string withReturn = QueryHelpers.AddQueryString(Path, ReturnUrlParameter, returnUrl);
        return QueryHelpers.AddQueryString(withReturn, ReasonParameter, reason);
    }

    /// <summary>
    /// Whether a return address is a page of this portal: a path that starts with a single '/'.
    /// Anything else ("//evil.example", "https://…", "/\evil") could send the person to another site.
    /// </summary>
    public static bool IsPortalPath(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl[0] != '/')
        {
            return false;
        }

        if (returnUrl.Length == 1)
        {
            return true;
        }

        return returnUrl[1] != '/' && returnUrl[1] != '\\';
    }
}
