// Compiles into the TEST project (project setup's csproj links Common/Testing/**), so it carries
// every using the test project's GlobalUsings.cs does not provide.
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RouteGo.App.Infrastructure;
using RouteGo.App.Infrastructure.Pipeline;
using RouteGo.App.Tests;

namespace RouteGo.App.Common.Testing;

/// <summary>
/// Builds the host every slice test runs against, on top of project setup's stamped AppFactory
/// (which owns the Development pin, so secret resolution never reaches AWS from a machine or a
/// build agent with no role). Seeds the configuration the host requires at startup, swaps the
/// "Backend" client's primary handler for the recording FakeBackend, and hands out a client that
/// does not follow redirects, so PRG responses are assertable (references/host-setup.md).
/// </summary>
public static class TestHost
{
    // A bare origin, so a stub matches the request path a handler builds and nothing depends on
    // whichever environment's base URL happens to be configured on the machine.
    private const string IdentityOrigin = "https://identity.test/";
    private const string NotificationOrigin = "https://notification.test/";
    private const string FleetOrigin = "https://fleet.test/";
    private const string SeatInventoryOrigin = "https://seat-inventory.test/";
    private const string SearchOrigin = "https://search.test/";
    private const string BookingOrigin = "https://booking.test/";
    private const string ReportingOrigin = "https://reporting.test/";
    private const string AuditOrigin = "https://audit.test/";

    /// <summary>Where every test host shows bus photos from.</summary>
    public const string PhotoDeliveryOrigin = "https://photos.test";

    /// <summary>The photo storage every test host lets the browser send bus photos to.</summary>
    public const string PhotoUploadOrigin = "https://uploads.test";

    /// <summary>The payment page's origin every test host lets a booking page send the browser to.</summary>
    public const string PaymentPageOrigin = "https://pay.test";

    /// <summary>The ticket bucket name every test host is configured with.</summary>
    public const string TicketBucket = "routego-tickets-test";

    /// <summary>The internal key every test host presents to Notification.</summary>
    public const string TestInternalKey = "test-internal-key";

    /// <summary>
    /// What every test host must read for these keys, whatever the machine around it says. Seeded
    /// into the process environment block once (below), because the host reads its configuration
    /// from environment variables and a few classes read the block directly.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Settings { get; } =
        new Dictionary<string, string?>
        {
            [ConfigKeys.IdentityUrl] = IdentityOrigin,
            [ConfigKeys.NotificationUrl] = NotificationOrigin,
            [ConfigKeys.FleetUrl] = FleetOrigin,
            [ConfigKeys.SeatInventoryUrl] = SeatInventoryOrigin,
            [ConfigKeys.SearchUrl] = SearchOrigin,
            [ConfigKeys.BookingUrl] = BookingOrigin,
            [ConfigKeys.ReportingUrl] = ReportingOrigin,
            [ConfigKeys.AuditUrl] = AuditOrigin,
            [ConfigKeys.PhotoDeliveryOrigin] = PhotoDeliveryOrigin,
            [ConfigKeys.PhotoUploadOrigin] = PhotoUploadOrigin,
            [ConfigKeys.PaymentPageOrigin] = PaymentPageOrigin,
            [ConfigKeys.TicketBucket] = TicketBucket,
            // The internal key Notification's stubs see; Development reads secrets from configuration.
            [InternalKeyHandler.SecretName] = TestInternalKey,
            // FILLED BY the sibling skills and feature work: one entry per key the host requires at
            // startup (RequireKeys) — skill-login-integration adds ClientId / IamBaseUrl / LoginUrl.
        };

    static TestHost()
    {
        foreach (KeyValuePair<string, string?> setting in Settings)
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }
    }

    /// <summary>Host with the "Backend" client's primary handler replaced by the recording stub.</summary>
    public static WebApplicationFactory<Program> CreateFactory(FakeBackend backend)
    {
        return CreateFactory(services =>
        {
            // FILLED BY skill-login-integration: services.AddTestAuthSession(); — signs the test host
            // in, so the authentication middleware renders the page instead of redirecting to login.
            services.AddHttpClient(HttpClients.Identity)
                .ConfigurePrimaryHttpMessageHandler(() => backend);
        });
    }

    /// <summary>
    /// Host for a slice needing more than the stubbed backend — a pinned clock, a second named
    /// client. The delegate replaces the default service setup rather than adding to it, so a
    /// caller that needs the backend stub registers it too.
    /// </summary>
    public static WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection> configureServices)
    {
        return new AppFactory(configureServices);
    }

    /// <summary>
    /// Host for a slice whose settings arrive in a SECRET rather than in configuration. The name is
    /// the same constant the composition root resolves, so the test exercises the real secret
    /// contract; pass a null value to exercise the secret being absent entirely.
    /// </summary>
    public static WebApplicationFactory<Program> CreateFactory(
        string secretName, string? secretJson, Action<IServiceCollection> configureServices)
    {
        return new AppFactory(secretName, secretJson, configureServices);
    }

    /// <summary>
    /// Client with an https base address — every cookie the app issues is Secure, and a plain http
    /// client would silently drop them all — that does not follow redirects, so a test can assert
    /// the Location a successful POST redirects to instead of the page it lands on.
    /// </summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var options = new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        };
        return factory.CreateClient(options);
    }
}
