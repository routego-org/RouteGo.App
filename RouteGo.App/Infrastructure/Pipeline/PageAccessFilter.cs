using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// The portal's own page protection, decided from the session alone before a page runs, so a page
/// the person may not open makes no service call. Visitors on a page that needs sign-in are sent
/// to sign in (and brought back); a signed-in person on a page their role may not open sees the
/// no-permission message at the same address; a signed-in person on a visitor-only page goes Home.
/// The services check roles again — this is never the only protection.
/// </summary>
public sealed class PageAccessFilter : IAsyncPageFilter
{
    private const string NoPermissionView = "/Features/Shared/_NoPermission.cshtml";
    private const string AdminPagesPrefix = "/Admin/";
    private const string OperatorPagesPrefix = "/Operator/";
    private const string BookingPagesPrefix = "/Bookings/";

    // The page access table of the home-and-navigation spec, by page name. Every other page under
    // /Operator/ is a Fleet page for operators only. A page missing from it needs sign-in, so a
    // forgotten entry fails closed rather than open.
    private static readonly Dictionary<string, PageAccessLevel> _pages =
        new Dictionary<string, PageAccessLevel>(StringComparer.Ordinal)
        {
            ["/Home/Home/Home"] = PageAccessLevel.Everyone,
            ["/Auth/Verify/Verify"] = PageAccessLevel.Everyone,
            ["/Error/Error"] = PageAccessLevel.Everyone,
            ["/Buses/PublicBus/PublicBus"] = PageAccessLevel.Everyone,
            ["/Seats/PublicSeatMap/PublicSeatMap"] = PageAccessLevel.Everyone,
            ["/Search/BusSearch/BusSearch"] = PageAccessLevel.Everyone,
            ["/Auth/Login/Login"] = PageAccessLevel.VisitorsOnly,
            ["/Auth/Register/Register"] = PageAccessLevel.VisitorsOnly,
            ["/Auth/ResendVerification/ResendVerification"] = PageAccessLevel.VisitorsOnly,
            ["/Operator/Apply/Apply"] = PageAccessLevel.VisitorsOnly,
            ["/Operator/Reapply/Reapply"] = PageAccessLevel.VisitorsOnly,
            ["/Profile/Profile/Profile"] = PageAccessLevel.SignedIn,
            ["/Auth/Logout/Logout"] = PageAccessLevel.SignedIn,
            ["/Tickets/Ticket/Ticket"] = PageAccessLevel.CustomerOrAdmin,
        };

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
    {
        return Task.CompletedTask;
    }

    public async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        PageAccessLevel level = LevelOf(context.ActionDescriptor.ViewEnginePath);
        IActionResult? refusal = Decide(level, context.HttpContext.User);
        if (refusal != null)
        {
            context.Result = refusal;
            return;
        }

        await next();
    }

    /// <summary>The access level of a page, by its page name ("/Auth/Login/Login").</summary>
    public static PageAccessLevel LevelOf(string pageName)
    {
        if (pageName.StartsWith(AdminPagesPrefix, StringComparison.Ordinal))
        {
            return PageAccessLevel.AdminOnly;
        }

        if (_pages.TryGetValue(pageName, out PageAccessLevel level))
        {
            return level;
        }

        if (pageName.StartsWith(OperatorPagesPrefix, StringComparison.Ordinal))
        {
            return PageAccessLevel.OperatorOnly;
        }

        if (pageName.StartsWith(BookingPagesPrefix, StringComparison.Ordinal))
        {
            return PageAccessLevel.CustomerOnly;
        }

        return PageAccessLevel.SignedIn;
    }

    private static IActionResult? Decide(PageAccessLevel level, ClaimsPrincipal user)
    {
        bool isSignedIn = user.Identity != null && user.Identity.IsAuthenticated;
        if (level == PageAccessLevel.Everyone)
        {
            return null;
        }

        if (level == PageAccessLevel.VisitorsOnly)
        {
            if (isSignedIn)
            {
                return new RedirectResult("/");
            }
            return null;
        }

        if (!isSignedIn)
        {
            return new ChallengeResult(PortalSession.Scheme);
        }

        if (level == PageAccessLevel.AdminOnly && !user.IsInRole(PortalSession.RoleAdmin))
        {
            return NoPermission();
        }

        if (level == PageAccessLevel.OperatorOnly && !user.IsInRole(PortalSession.RoleOperator))
        {
            return NoPermission();
        }

        if (level == PageAccessLevel.CustomerOnly && !user.IsInRole(PortalSession.RoleCustomer))
        {
            return NoPermission();
        }

        if (level == PageAccessLevel.CustomerOrAdmin && !IsCustomerOrAdmin(user))
        {
            return NoPermission();
        }

        return null;
    }

    private static bool IsCustomerOrAdmin(ClaimsPrincipal user)
    {
        bool isCustomer = user.IsInRole(PortalSession.RoleCustomer);
        bool isAdmin = user.IsInRole(PortalSession.RoleAdmin);
        return isCustomer || isAdmin;
    }

    private static ViewResult NoPermission()
    {
        return new ViewResult { ViewName = NoPermissionView, StatusCode = StatusCodes.Status403Forbidden };
    }
}
