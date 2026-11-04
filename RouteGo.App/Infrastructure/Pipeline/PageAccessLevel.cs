namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>Who may open a page.</summary>
public enum PageAccessLevel
{
    Everyone,
    VisitorsOnly,
    SignedIn,
    CustomerOnly,
    CustomerOrAdmin,
    OperatorOnly,
    AdminOnly,
}
