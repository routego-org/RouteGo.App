using RouteGo.App.Infrastructure;
// FILLED BY skills-aspnetcore-ui-vertical-slice — the usings for what it wires below (e.g. using RouteGo.App.Infrastructure.Pgsql;
// when the first data slice lands). Non-System usings only: System.* sort first (IDE0055).
using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.WebUtilities;
using RouteGo.App.Infrastructure.Pipeline;

// Configuration reaches this app through ONE channel in every environment: environment variables,
// flat-named so there is no ':' / '__' separator to mistype (Infrastructure/ConfigKeys.cs). The
// deployed values come from the deployment template; the local ones from LocalEntryPoints, which
// MUST run before the builder is created — its environment-variable provider snapshots the process
// block once, so anything set after that line never reaches IConfiguration
// (references/configuration.md).
LocalEntryPoints.Apply();
// FILLED BY skills-aspnetcore-ui-vertical-slice only under its direct-database exception
// (references/data-access.md) — BEFORE the builder, so nothing can query first: Dapper bakes its name
// mapping per (query, type) at first execution, so a late call is a silent no-op (skills-db-postgres
// #11/#25; its check_dapper_mapping.py D3 anchors on this line). The timeout is DERIVED from this
// app's request deadline, with the derivation in a why-comment here:
//   DapperConfiguration.Apply(commandTimeout: TimeSpan.FromSeconds(...));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Deployed, the non-secret settings live in SSM Parameter Store so they change without a rebuild;
// loaded after the environment variables so the parameter store is the source of truth there.
// Development never calls AWS — LocalEntryPoints supplies the same keys.
builder.Configuration.AddPortalParameters(builder.Environment);

// DI scope validation, on in EVERY environment (skills-db-postgres #26). CreateBuilder turns
// these on in Development only, so a captive dependency throws on a laptop and goes unnoticed in
// production: a singleton that takes a scoped IWriterConnectionFactory or ICurrentActor resolves
// it from the ROOT scope and freezes the first request's actor for the life of the process, and
// every later user's writes are stamped with that identity. ValidateOnBuild catches the capture
// at startup; ValidateScopes catches a scoped service pulled from app.Services at runtime.
// A startup failure here is the finding, not a regression - read the exception, fix the capture.
builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

// Fail at startup naming every missing key at once, rather than on whichever request first needs
// one. List only what the app cannot start without; a legitimately optional key is left out.
// FILLED BY the sibling slice skill as it introduces keys:
//   builder.Configuration.RequireKeys(ConfigKeys.BackendBaseUrl, ConfigKeys.ClientId);
builder.Configuration.RequireKeys(
    ConfigKeys.IdentityUrl, ConfigKeys.NotificationUrl, ConfigKeys.FleetUrl, ConfigKeys.SeatInventoryUrl,
    ConfigKeys.SearchUrl, ConfigKeys.BookingUrl, ConfigKeys.ReportingUrl, ConfigKeys.AuditUrl, ConfigKeys.TicketBucket);

// Log levels, in code for the same reason every other setting is: one place, and no appsettings
// file to disagree with it. Everything defaults to Information; the framework's own request/routing
// chatter stays at Warning so the log group is not flooded, except in Development where it
// is the most useful thing in the console. A deployed run can still be turned up without a code
// change: Logging__LogLevel__Default is an ordinary environment variable on the container.
LogLevel frameworkLogLevel = LogLevel.Warning;
if (builder.Environment.IsDevelopment())
{
    frameworkLogLevel = LogLevel.Information;
}

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", frameworkLogLevel);
// The request line carries query values such as the email verification token, which must never
// reach a log line — so the framework's per-request lines stay off in every environment.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// Secrets: standard AWS Secrets Manager access (references/secrets.md). ONE fixed name per secret;
// the SOURCE is the environment — Development reads configuration, everything else Secrets Manager.
builder.Services.AddSingleton<SecretResolver>();

// Data Protection: key ring persisted on the /keys volume outside Development, so antiforgery
// tokens / TempData / auth cookies survive a container restart (references/data-protection.md).
builder.Services.AddProjectDataProtection(builder.Environment);

// Infrastructure ports — registered once. No per-slice registrations.
builder.Services.AddSingleton<IClock, SystemClock>();
// FILLED BY skills-aspnetcore-ui-vertical-slice when the first data slice lands (direct-database
//   exception): the reader/writer connection-port registrations (+ any other ports).

builder.Services.AddRazorPages(o =>
{
    o.RootDirectory = "/Features";
    // FILLED BY skills-aspnetcore-ui-vertical-slice: o.Conventions.Add(new SliceRouteConvention());
    o.Conventions.Add(new SliceRouteConvention());
})
.AddMvcOptions(o =>
{
    // Who may open which page, decided before any page runs; and an expired form becomes a
    // message on a fresh form instead of a bare 400.
    o.Filters.Add<PageAccessFilter>();
    o.Filters.Add<FormExpiredFilter>();
    // Every field rule and its wording comes from the slice's validator (the spec's words), so the
    // framework's implicit "The X field is required." for non-nullable strings stays off.
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});

// ─────────────────────────────────────────────────────────────────────────────
// FILLED BY skills-aspnetcore-ui-vertical-slice:
//   the "Backend" IHttpClientFactory client + BackendAuthHandler; AddHttpContextAccessor;
//   ForwardedHeaders; SecurityHeaders/CSP.
// ZERO per-slice registrations — PageModels construct their own handler + validator.
// ─────────────────────────────────────────────────────────────────────────────
// Every cookie is Secure-only. Deployed, the request reaches the portal as https through
// CloudFront and Nginx (X-Forwarded-Proto). The one exception is a developer's machine, where the
// portal runs on plain http://localhost:8080: there the cookies follow the request instead.
CookieSecurePolicy cookieSecurity = CookieSecurePolicy.Always;
if (builder.Environment.IsDevelopment())
{
    cookieSecurity = CookieSecurePolicy.SameAsRequest;
}

// The session: an encrypted cookie holding the person's claims and Identity's token pair
// (Common/UI/PortalSession.cs). HttpOnly, Secure and SameSite=Lax; a page that needs sign-in sends
// a visitor to the sign-in page with the page as the return address.
builder.Services.AddAuthentication(PortalSession.Scheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "routego.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = cookieSecurity;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = SignInAddress.Path;
        o.ReturnUrlParameter = SignInAddress.ReturnUrlParameter;
        o.SlidingExpiration = false;
        o.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect(QueryHelpers.AddQueryString(
                context.RedirectUri, SignInAddress.ReasonParameter, SignInAddress.ReasonSignInRequired));
            return Task.CompletedTask;
        };
    });
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.SecurePolicy = cookieSecurity;
    o.Cookie.HttpOnly = true;
});
builder.Services.Configure<CookieTempDataProviderOptions>(o =>
{
    o.Cookie.SecurePolicy = cookieSecurity;
    o.Cookie.HttpOnly = true;
});
builder.Services.Configure<CookiePolicyOptions>(o =>
{
    o.Secure = cookieSecurity;
    o.HttpOnly = HttpOnlyPolicy.Always;
});

// One named client per service (Common/HttpClients.cs). AddApiClient (project setup's) reads the
// base URL from ConfigKeys and normalises the trailing slash — NEVER assign BaseAddress by hand.
// Handler order, outermost first: correlation id -> the person's token (renewing the session when
// needed) -> one time budget per attempt, read-only retry, outage -> UPSTREAM_* problem.
builder.Services.AddSingleton<SessionRenewal>();
builder.Services.AddTransient<CorrelationIdHandler>();
builder.Services.AddTransient<IdentityTokenHandler>();
builder.Services.AddTransient<UpstreamFailureHandler>();
// Primary handler below: no cookie jar, because the pooled handler is shared by every person, so a
// cookie stored by one response would be sent on another person's call. Connections recycle every
// 2 minutes so a moved private address is picked up; the handler itself then never needs replacing.
builder.Services.AddApiClient(builder.Configuration, HttpClients.Identity, ConfigKeys.IdentityUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<IdentityTokenHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
// Notification's internal operations (the admin email log) take the internal key, never the
// person's token: no IdentityTokenHandler here, so Notification's UNAUTHORIZED (a wrong key) can
// never end the admin's session. Same time budget as Identity (10 s per attempt, reads retried
// once, a resend never retried), chosen 2026-10-09: an SES answer normally takes well under a second.
builder.Services.AddTransient<InternalKeyHandler>();
builder.Services.AddApiClient(builder.Configuration, HttpClients.Notification, ConfigKeys.NotificationUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<InternalKeyHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
// Fleet takes the same Identity access token, so it shares Identity's chain: the token handler
// renews the session before a call and ends it on Fleet's UNAUTHORIZED. Calls a slice leaves
// unmarked (the public bus page) go without a token. Same time budget as Identity (10 s per
// attempt, reads retried once, writes never retried): Fleet answers from its own database.
builder.Services.AddApiClient(builder.Configuration, HttpClients.Fleet, ConfigKeys.FleetUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<IdentityTokenHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
// Seat Inventory takes the same Identity access token, so it shares Fleet's chain; the public seat
// map is sent unmarked, without a token. Same time budget as Fleet (10 s per attempt, reads retried
// once, a block, unblock or hold cancellation never retried), chosen 2026-10-21: Seat Inventory
// answers from its own table, and a retried write could act twice.
builder.Services.AddApiClient(builder.Configuration, HttpClients.SeatInventory, ConfigKeys.SeatInventoryUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<IdentityTokenHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
// Search's two operations (bus search, city suggestions) are public, so no token handler is
// chained: the person's token is never sent, and Search can never end a session. Same time budget
// as the other services (10 s per attempt, reads retried once), chosen 2026-10-21: Search answers
// from its own listing table, and both operations are reads.
builder.Services.AddApiClient(builder.Configuration, HttpClients.Search, ConfigKeys.SearchUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
// Booking, Reporting and Audit take the signed-in person's Identity token on every call, so they
// share Fleet's chain. Same time budget (10 s per attempt, reads retried once, writes — create a
// booking, cancel, force-cancel — never retried), chosen 2026-10-31: a retried booking could
// hold seats twice and a retried cancel could refund twice.
builder.Services.AddApiClient(builder.Configuration, HttpClients.Booking, ConfigKeys.BookingUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<IdentityTokenHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
// Ticket Generator has no API: the ticket page reads its private bucket directly, through the
// platform's own identity (read permission on tickets/* and the bucket listing, nothing else). Time
// budget as for the services (10 s per attempt, one retry): both calls are reads, and the page
// shows "not available" instead of waiting longer. Chosen 2026-10-31.
builder.Services.AddSingleton<IAmazonS3>(_ =>
{
    AWSOptions options = builder.Configuration.GetAWSOptions();
    options.DefaultClientConfig.Timeout = TimeSpan.FromSeconds(10);
    options.DefaultClientConfig.MaxErrorRetry = 1;
    return options.CreateServiceClient<IAmazonS3>();
});
builder.Services.AddSingleton<ITicketStorage, S3TicketStorage>();
builder.Services.AddApiClient(builder.Configuration, HttpClients.Reporting, ConfigKeys.ReportingUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<IdentityTokenHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
builder.Services.AddApiClient(builder.Configuration, HttpClients.Audit, ConfigKeys.AuditUrl)
    .AddHttpMessageHandler<CorrelationIdHandler>()
    .AddHttpMessageHandler<IdentityTokenHandler>()
    .AddHttpMessageHandler<UpstreamFailureHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
// traceparent propagation is automatic (Activity-based, built into HttpClient) — no hand-threaded id.

builder.Services.AddHttpContextAccessor();             // ScriptNonceTagHelper reads the per-request nonce
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
});
// CSP origins beyond 'self', the CDN and the design system's defaults (Google Fonts, the flags CDN)
// are declared HERE, never by editing the middleware (references/security.md):
//   o.ConnectSources.Add("https://*.s3.ap-south-1.amazonaws.com"); o.FormActions.Add(loginOrigin);
// Bus photos are shown from Fleet's photo delivery network, and the browser sends new photos
// straight to photo storage (never through the portal). Both addresses differ per deployment and
// are optional: without them photos simply do not load or send. Read here, so a bad value stops
// the app at startup.
string? photoDelivery = builder.Configuration.ReadOrigin(ConfigKeys.PhotoDeliveryOrigin);
string? photoUpload = builder.Configuration.ReadOrigin(ConfigKeys.PhotoUploadOrigin);
// "Pay" and "Pay now" answer a form post with a redirect to the payment gateway's own page, and the
// browser checks that redirect against form-action, so the gateway's origin is allowed there.
string? paymentPage = builder.Configuration.ReadOrigin(ConfigKeys.PaymentPageOrigin);
builder.Services.Configure<SecurityHeadersOptions>(o =>
{
    // The stamped defaults are the RouteGo design system's font and flag origins; the portal uses
    // Bootstrap from its own wwwroot, so the policy stays 'self' only.
    o.StyleSources.Clear();
    o.ImageSources.Clear();
    o.FontSources.Clear();

    if (photoDelivery != null)
    {
        o.ImageSources.Add(photoDelivery);
    }

    if (photoUpload != null)
    {
        o.ConnectSources.Add(photoUpload);
    }

    if (paymentPage != null)
    {
        o.FormActions.Add(paymentPage);
    }
});

WebApplication app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();       // the id is on every log line and every service call
// FILLED BY skills-aspnetcore-ui-vertical-slice: UseExceptionHandler("/error"); SecurityHeadersMiddleware;
//   dev-only UseStaticFiles; UseCookiePolicy.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");              // dev shows the framework developer page
}
// An address no page owns answers 404 through the same page as a failure; it runs after routing so
// a visitor is never sent to sign in for a page that does not exist.
app.UseStatusCodePagesWithReExecute("/error", "?statusCode={0}");
app.UseMiddleware<SecurityHeadersMiddleware>();     // CSP (+ nonce) + header set on every response
// Every environment: CloudFront in front of the edge instance caches /css, /js and /lib, so the
// portal itself is the origin for its static files (there is no separate asset bucket).
app.UseStaticFiles();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseMiddleware<SessionEndedMiddleware>();        // a session that cannot continue -> sign in again
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok("healthy")); // placeholder until the first slice lands

app.Run();

public partial class Program { } // exposes Program to WebApplicationFactory<Program> in tests
