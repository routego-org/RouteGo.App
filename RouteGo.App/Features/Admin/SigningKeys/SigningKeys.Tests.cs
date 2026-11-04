using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Admin.SigningKeys;

public sealed class SigningKeysTests
{
    private const string SigningKeysPath = "/admin/signing-keys";
    private const string KeySetPath = "/.well-known/jwks.json";

    [Fact]
    public async Task Get_Admin_ListsPublishedKeysInIdentitysOrder()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, KeySetPath, () => KeySet(Key("k-2026-09"), Key("k-2026-10")));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);
        string[] columns = new[] { "Key ID", "Key type", "Algorithm", "Use" };

        // Act
        IDocument page = await form.GetPageAsync(SigningKeysPath);

        // Assert
        Assert.Equal("Signing keys", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(columns, Texts(page, "[data-keys-table] th"));
        Assert.Equal(new[] { "k-2026-09", "RSA", "RS256", "sig" }, Texts(page, "[data-key-row='k-2026-09'] td"));
        Assert.Equal(new[] { "k-2026-10", "RSA", "RS256", "sig" }, Texts(page, "[data-key-row='k-2026-10'] td"));
        Assert.Equal(new[] { "k-2026-09", "k-2026-10" }, page.QuerySelectorAll("[data-key-row]").Select(row => row.GetAttribute("data-key-row")!).ToArray());
    }

    [Fact]
    public async Task Get_Admin_OffersNoActionAndSaysWhoChangesKeys()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, KeySetPath, () => KeySet(Key("k-2026-10")));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(SigningKeysPath);

        // Assert
        Assert.Equal(
            "These are the public keys other RouteGo services use to check that a sign-in pass really comes from RouteGo Identity. They contain no secrets. Keys are changed by the platform engineer, not on this page.",
            page.QuerySelector("[data-keys-intro]")!.TextContent);
        Assert.Empty(page.QuerySelectorAll("main form[method='post'], [data-keys-table] button, [data-keys-table] form"));
        Assert.Null(fake.Requests.Last(request => request.RequestUri!.AbsolutePath == KeySetPath).Headers.Authorization);
    }

    [Fact]
    public async Task Get_Customer_ShowsNoPermissionWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));

        // Act
        HttpResponseMessage response = await client.GetAsync(SigningKeysPath);
        string html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("You don&#x27;t have permission to open this page.", html);
        Assert.DoesNotContain(fake.Requests, request => request.RequestUri!.AbsolutePath == KeySetPath);
    }

    [Fact]
    public async Task Get_IdentityUnreachable_ShowsUpstreamMessage()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, KeySetPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(SigningKeysPath);

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-keys-table]"));
    }

    [Fact]
    public async Task Get_NoKeysPublished_TellsAdminToCallPlatformEngineer()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, KeySetPath, () => KeySet());
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(SigningKeysPath);

        // Assert
        Assert.Equal(
            "Identity is not publishing any keys right now. Sign-in will not work until it does. Tell the platform engineer.",
            page.QuerySelector("[data-keys-empty]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-keys-table]"));
    }

    private static async Task<FormClient> SignInAsync(WebApplicationFactory<Program> factory, FakeBackend fake, string role)
    {
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("priya@example.com", role));
        return new FormClient(client);
    }

    private static object Key(string kid)
    {
        return new { kty = "RSA", use = "sig", alg = "RS256", kid, n = "sXchmodulus", e = "AQAB" };
    }

    private static HttpResponseMessage KeySet(params object[] keys)
    {
        return FakeBackend.Json(new { keys }, HttpStatusCode.OK);
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
