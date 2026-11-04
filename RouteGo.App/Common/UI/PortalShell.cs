using System.Globalization;
using System.Security.Claims;

namespace RouteGo.App.Common.UI;

/// <summary>
/// What the portal shell (the shared layout) shows around every page: whether someone is signed
/// in, their email and the letter on their avatar, their role, and which menu link is the page
/// being shown. Prepared here so the layout only prints values and toggles classes.
/// </summary>
public sealed class PortalShell
{
    private readonly string _currentPath;

    private PortalShell(ClaimsPrincipal user, string currentPath)
    {
        _currentPath = currentPath;
        IsSignedIn = user.Identity != null && user.Identity.IsAuthenticated;
        IsAdmin = user.IsInRole(PortalSession.RoleAdmin);
        IsOperator = user.IsInRole(PortalSession.RoleOperator);
        IsCustomer = user.IsInRole(PortalSession.RoleCustomer);
        HasSideMenu = IsAdmin || IsOperator;
        Email = "";
        AvatarLetter = "";
        RoleLabel = "";
        if (!IsSignedIn)
        {
            return;
        }

        Email = user.FindFirst(PortalSession.EmailClaim)!.Value;
        RoleLabel = Messages.RoleLabel(user.FindFirst(PortalSession.RoleClaim)!.Value);
        AvatarLetter = Email.Substring(0, 1).ToUpper(CultureInfo.InvariantCulture);
    }

    public bool IsSignedIn { get; }

    public bool IsAdmin { get; }

    public bool IsOperator { get; }

    public bool IsCustomer { get; }

    /// <summary>Admins and operators get the side menu with their work pages; everyone else gets the top bar only.</summary>
    public bool HasSideMenu { get; }

    public string Email { get; }

    /// <summary>The first letter of the email, shown in the round profile button.</summary>
    public string AvatarLetter { get; }

    /// <summary>The role as the person reads it (Customer, Operator, Admin).</summary>
    public string RoleLabel { get; }

    /// <summary>The shell for the request being rendered.</summary>
    public static PortalShell From(HttpContext httpContext)
    {
        string currentPath = httpContext.Request.Path.Value!;
        return new PortalShell(httpContext.User, currentPath);
    }

    /// <summary>True when the menu link points at the page being shown, so it is highlighted.</summary>
    public bool IsCurrent(string href)
    {
        return string.Equals(_currentPath, href, StringComparison.OrdinalIgnoreCase);
    }
}
