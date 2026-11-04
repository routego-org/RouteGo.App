namespace RouteGo.App.Infrastructure;

/// <summary>
/// The name of every environment-supplied configuration value, spelled exactly once. The names are
/// deliberately FLAT: each maps 1:1 to an environment variable of the same spelling, so no ':' or
/// '__' separator can be mistyped between the deployment template, the pipeline, and the reader
/// here. LocalEntryPoints and every consumer go through these constants, so a rename is a compile
/// error rather than a value that silently arrives null (references/configuration.md).
/// </summary>
public static class ConfigKeys
{
    /// <summary>The AWS SDK's own region variable, and the ONLY region source the app reads.
    /// Lambda sets it; LocalEntryPoints seeds it on a laptop. There is deliberately no app-owned
    /// AWS:Region key that could disagree with the runtime's own value.</summary>
    public const string AwsRegion = "AWS_REGION";

    // ══════════════════════════════════════════════════════════════════════════
    // FILLED BY the sibling slice skill and by feature work: one const per value
    // that varies by environment — a base URL, a client id, a public origin.
    //
    //   public const string BackendBaseUrl = "BackendBaseUrl";
    //
    // Adding one is four more edits, none of them a new file: a local value in
    // LocalEntryPoints, a parameter + env var in the deployment template, a
    // pipeline variable, and a RequireKeys entry if the app cannot start without
    // it. NEVER add a configuration key to an appsettings*.json.
    //
    // Otel:Exporter:* and AWS:Profile do NOT belong here — see
    // references/configuration.md for why those two keep their nested names.
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>Identity's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>identity-url</c> (a private address); locally Identity's own run profile.</summary>
    public const string IdentityUrl = "IdentityUrl";

    /// <summary>Notification's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>notification-url</c> (a private address); locally Notification's own run profile.</summary>
    public const string NotificationUrl = "NotificationUrl";

    /// <summary>Fleet's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>fleet-url</c> (a private address); locally Fleet's own run profile.</summary>
    public const string FleetUrl = "FleetUrl";

    /// <summary>Seat Inventory's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>seat-inventory-url</c> (a private address); locally Seat Inventory's own run profile.</summary>
    public const string SeatInventoryUrl = "SeatInventoryUrl";

    /// <summary>Search's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>search-url</c> (a private address); locally Search's own run profile.</summary>
    public const string SearchUrl = "SearchUrl";

    /// <summary>Booking's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>booking-url</c> (a private address); locally Booking's own run profile.</summary>
    public const string BookingUrl = "BookingUrl";

    /// <summary>Reporting's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>reporting-url</c> (a private address); locally Reporting's own run profile.</summary>
    public const string ReportingUrl = "ReportingUrl";

    /// <summary>Audit's base URL, with its trailing slash. Deployed it is the SSM parameter
    /// <c>audit-url</c> (a private address); locally Audit's own run profile.</summary>
    public const string AuditUrl = "AuditUrl";

    /// <summary>The name of the private bucket Ticket Generator stores e-tickets in, which the ticket
    /// page reads. Deployed it is the SSM parameter <c>ticket-bucket</c> (<c>routego-tickets-{account}</c>);
    /// locally the LocalStack bucket <c>routego-tickets-local</c>.</summary>
    public const string TicketBucket = "TicketBucket";

    /// <summary>The origin bus photos are shown from (Fleet's photo delivery network). Optional:
    /// deployed it is the SSM parameter <c>photo-delivery-origin</c>; left out, the security policy
    /// does not allow photos from anywhere but the portal.</summary>
    public const string PhotoDeliveryOrigin = "PhotoDeliveryOrigin";

    /// <summary>The origin of the photo storage the browser sends bus photos to (the host of
    /// Fleet's upload addresses). Optional: deployed it is the SSM parameter
    /// <c>photo-upload-origin</c>; left out, the browser cannot send photos.</summary>
    public const string PhotoUploadOrigin = "PhotoUploadOrigin";

    /// <summary>The origin of the payment gateway's page that "Pay" and "Pay now" send the browser
    /// to. Optional: deployed it is the SSM parameter <c>payment-page-origin</c>; left out, the
    /// security policy only lets forms go to the portal, so a browser that follows the redirect to
    /// the payment page is stopped.</summary>
    public const string PaymentPageOrigin = "PaymentPageOrigin";
}
