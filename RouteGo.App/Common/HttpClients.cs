namespace RouteGo.App.Common;

/// <summary>
/// The name of every named HttpClient this app registers — one entry per API it calls outbound.
/// Consumers pass one of these to IHttpClientFactory.CreateClient; the composition root registers
/// each one against its base-URL key in ConfigKeys through AddApiClient. Adding an API adds a line
/// here, not another file (references/configuration.md).
/// </summary>
public static class HttpClients
{
    // ══════════════════════════════════════════════════════════════════════════
    // FILLED BY the sibling slice skill and by feature work: one const per API
    // this app calls outbound.
    //
    //   public const string Backend = "Backend";
    //
    // Register it in the composition root, naming its base URL from ConfigKeys
    // and chaining whatever handler that API needs:
    //
    //   builder.Services.AddApiClient(builder.Configuration,
    //           HttpClients.Backend, ConfigKeys.BackendBaseUrl)
    //       .AddHttpMessageHandler<SomeAuthHandler>();
    //
    // Never assign a client's BaseAddress by hand — AddApiClient normalises the
    // trailing slash a versioned base URL depends on, and check_setup.py fails a
    // project that sets one anywhere else.
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>Identity: sign-up, sign-in, sessions, profile and account administration.</summary>
    public const string Identity = "Identity";

    /// <summary>Notification: the send log search and resend behind the admin email log (internal key, no person's token).</summary>
    public const string Notification = "Notification";

    /// <summary>Fleet: buses, photos, routes, schedules and trips (the person's token), and the public bus page (none).</summary>
    public const string Fleet = "Fleet";

    /// <summary>Seat Inventory: a trip's seats for its operator or an admin, blocks and hold cancellation (the person's token), and the public seat map (none).</summary>
    public const string SeatInventory = "SeatInventory";

    /// <summary>Search: bus search results and city suggestions for the search form, both public (never the person's token).</summary>
    public const string Search = "Search";

    /// <summary>Booking: bookings, cancellations and the passenger list, always with the person's token.</summary>
    public const string Booking = "Booking";

    /// <summary>Reporting: the operator dashboard, the admin summary and settlements, always with the person's token.</summary>
    public const string Reporting = "Reporting";

    /// <summary>Audit: the admin audit log search, always with the admin's token.</summary>
    public const string Audit = "Audit";
}
