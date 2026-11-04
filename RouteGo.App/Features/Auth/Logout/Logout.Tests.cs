using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using RouteGo.App.Features.Home.Home;

namespace RouteGo.App.Features.Auth.Logout;

public sealed class LogoutTests
{
    private const string LogoutPath = "/auth/logout";
    private const string RevokePath = "/auth/sessions/revoke";
    private const string RefreshPath = "/auth/sessions/refresh";

    [Fact]
    public async Task PostFromRoleMenu_SignedIn_RevokesAndArrivesHomeSignedOut()
    {
        // Arrange
        FakeBackend fake = HomeBackend();
        fake.Map(HttpMethod.Post, RevokePath, () => new HttpResponseMessage(HttpStatusCode.NoContent));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair tokens = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        await TestSignIn.SignInAsync(client, fake, tokens);
        var form = new FormClient(client);
        IDocument home = await form.GetPageAsync("/");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(home, "form[data-menu-sign-out]", new Dictionary<string, string>());
        IDocument after = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Equal("{\"refreshToken\":\"" + tokens.RefreshToken + "\"}", fake.RequestBodies.Last(body => body.Contains("refreshToken")));
        Assert.Equal("Bearer " + tokens.AccessToken, RevokeAuthorization(fake));
        Assert.Equal("You have signed out.", after.QuerySelector("[data-flash]")!.TextContent);
        Assert.NotNull(after.QuerySelector("[data-visitor-home]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Post_IdentityRefusesOrIsDown_StillSignsOut(HttpStatusCode status)
    {
        // Arrange
        FakeBackend fake = HomeBackend();
        fake.Map(HttpMethod.Post, RevokePath, () => IdentityStubs.Problem(status, "UNAUTHORIZED"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));
        var form = new FormClient(client);
        IDocument confirm = await form.GetPageAsync(LogoutPath);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, "form[data-logout-form]", new Dictionary<string, string>());
        IDocument after = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Equal("You have signed out.", after.QuerySelector("[data-flash]")!.TextContent);
        Assert.NotNull(after.QuerySelector("[data-visitor-home]"));
    }

    [Fact]
    public async Task Post_RenewalRefused_SkipsRevokeAndSignsOut()
    {
        // Arrange
        FakeBackend fake = HomeBackend();
        fake.Map(HttpMethod.Post, RefreshPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "REFRESH_TOKEN_INVALID"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair expired = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer, FixedClock.Now.AddMinutes(-2));
        await TestSignIn.SignInAsync(client, fake, expired);
        var form = new FormClient(client);
        IDocument confirm = await form.GetPageAsync(LogoutPath);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(confirm, "form[data-logout-form]", new Dictionary<string, string>());

        // Assert
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(fake.Requests, request => request.RequestUri!.AbsolutePath == RevokePath);
    }

    [Fact]
    public async Task Get_SignedIn_AsksBeforeSigningOut()
    {
        // Arrange
        FakeBackend fake = HomeBackend();
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));
        var form = new FormClient(client);

        // Act
        IDocument confirm = await form.GetPageAsync(LogoutPath);
        HttpResponseMessage stillSignedIn = await client.GetAsync("/auth/login");

        // Assert
        Assert.Equal("Sign out", confirm.QuerySelector("h1")!.TextContent);
        Assert.Equal("Do you want to sign out?", confirm.QuerySelector("[data-logout-question]")!.TextContent);
        Assert.Equal("Sign out", confirm.QuerySelector("form[data-logout-form] button")!.TextContent);
        Assert.Equal("/", confirm.QuerySelector("form[data-logout-form] a")!.GetAttribute("href"));
        Assert.Equal("Cancel", confirm.QuerySelector("form[data-logout-form] a")!.TextContent);
        Assert.Equal(HttpStatusCode.Redirect, stillSignedIn.StatusCode);
        Assert.DoesNotContain(fake.Requests, request => request.RequestUri!.AbsolutePath == RevokePath);
    }

    [Fact]
    public async Task Post_OneBrowser_OtherBrowserStaysSignedIn()
    {
        // Arrange
        FakeBackend fake = HomeBackend();
        fake.Map(HttpMethod.Post, RevokePath, () => new HttpResponseMessage(HttpStatusCode.NoContent));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient laptop = TestHost.CreateClient(factory);
        HttpClient phone = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(laptop, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));
        await TestSignIn.SignInAsync(phone, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));
        var laptopForm = new FormClient(laptop);
        IDocument confirm = await laptopForm.GetPageAsync(LogoutPath);

        // Act
        await laptopForm.SendFormAsync(confirm, "form[data-logout-form]", new Dictionary<string, string>());
        IDocument phoneHome = await new FormClient(phone).GetPageAsync("/");

        // Assert
        Assert.Equal("Welcome, Alice Rao.", phoneHome.QuerySelector("[data-welcome]")!.TextContent);
    }

    [Fact]
    public async Task Get_Visitor_SentToSignInWithReturnAddress()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(LogoutPath);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login?returnUrl=%2Fauth%2Flogout&reason=sign-in-required", response.Headers.Location!.PathAndQuery);
    }

    private static FakeBackend HomeBackend()
    {
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, "/me", () => HomeTests.ProfileAnswer("alice@example.com", "Alice Rao", "customer"));
        return fake;
    }

    private static string? RevokeAuthorization(FakeBackend fake)
    {
        HttpRequestMessage revoke = fake.Requests.Last(request => request.RequestUri!.AbsolutePath == RevokePath);
        return revoke.Headers.Authorization!.ToString();
    }
}
