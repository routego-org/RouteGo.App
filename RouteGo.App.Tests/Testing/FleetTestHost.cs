using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RouteGo.App.Common.Ports;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// The host every Fleet, seat, search, booking and report page test runs against: Identity, Fleet,
/// Seat Inventory, Search, Booking, Reporting and Audit each answered by their own recording stub, and the clock pinned.
/// Signs the test browser in through the sign-in page, so the calls carry the session's real access token.
/// </summary>
public sealed class FleetTestHost : IDisposable
{
    public FleetTestHost()
    {
        Factory = TestHost.CreateFactory(services =>
        {
            services.AddHttpClient(HttpClients.Identity).ConfigurePrimaryHttpMessageHandler(() => Identity);
            services.AddHttpClient(HttpClients.Fleet).ConfigurePrimaryHttpMessageHandler(() => Fleet);
            services.AddHttpClient(HttpClients.SeatInventory).ConfigurePrimaryHttpMessageHandler(() => SeatInventory);
            services.AddHttpClient(HttpClients.Search).ConfigurePrimaryHttpMessageHandler(() => Search);
            services.AddHttpClient(HttpClients.Booking).ConfigurePrimaryHttpMessageHandler(() => Booking);
            services.AddHttpClient(HttpClients.Reporting).ConfigurePrimaryHttpMessageHandler(() => Reporting);
            services.AddHttpClient(HttpClients.Audit).ConfigurePrimaryHttpMessageHandler(() => Audit);
            services.AddSingleton<IClock, FixedClock>();
            services.AddSingleton<ITicketStorage>(TicketStorage);
        });
    }

    public FakeBackend Booking { get; } = new FakeBackend();

    public FakeBackend Reporting { get; } = new FakeBackend();

    public FakeBackend Audit { get; } = new FakeBackend();

    /// <summary>The private ticket storage the ticket page reads, in place of the S3 bucket.</summary>
    public FakeTicketStorage TicketStorage { get; } = new FakeTicketStorage();

    public FakeBackend Search { get; } = new FakeBackend();

    public FakeBackend Identity { get; } = new FakeBackend();

    public FakeBackend Fleet { get; } = new FakeBackend();

    public FakeBackend SeatInventory { get; } = new FakeBackend();

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The access token the signed-in browser's Fleet calls must carry.</summary>
    public string? AccessToken { get; private set; }

    /// <summary>A browser signed in with the role (an Operator unless the test says otherwise).</summary>
    public async Task<FormClient> SignInAsync(string role)
    {
        HttpClient client = await SignInClientAsync(role);
        return new FormClient(client);
    }

    /// <summary>The signed-in browser as a bare client, for a test that asserts a status or a redirect.</summary>
    public async Task<HttpClient> SignInClientAsync(string role)
    {
        HttpClient client = TestHost.CreateClient(Factory);
        TokenPair tokens = IdentityStubs.Tokens("ravi@busco.example", role);
        AccessToken = tokens.AccessToken;
        await TestSignIn.SignInAsync(client, Identity, tokens);
        return client;
    }

    /// <summary>How many requests reached Fleet with the method and path.</summary>
    public int FleetCalls(HttpMethod method, string path)
    {
        return Fleet.Requests.Count(request => request.Method == method && request.RequestUri!.AbsolutePath == path);
    }

    /// <summary>How many requests reached Seat Inventory with the method and path.</summary>
    public int SeatInventoryCalls(HttpMethod method, string path)
    {
        return SeatInventory.Requests.Count(request => request.Method == method && request.RequestUri!.AbsolutePath == path);
    }

    public void Dispose()
    {
        Factory.Dispose();
    }
}
