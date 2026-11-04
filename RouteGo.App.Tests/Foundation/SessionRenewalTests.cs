using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using RouteGo.App.Features.Home.Home;

namespace RouteGo.App.Tests.Foundation;

/// <summary>
/// Staying signed in (session-and-sign-out, CAP-UI-AUTH-005), exercised through Home — the first
/// page that calls Identity with the person's token.
/// </summary>
public sealed class SessionRenewalTests
{
    private const string ProfilePath = "/me";
    private const string RefreshPath = "/auth/sessions/refresh";

    [Fact]
    public async Task Get_AccessTokenExpired_RenewsQuietlyAndUsesNewToken()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair renewed = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, RefreshPath, () => IdentityStubs.TokenAnswer(renewed));
        fake.Map(HttpMethod.Get, ProfilePath, () => HomeTests.ProfileAnswer("alice@example.com", "Alice Rao", "customer"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair expired = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer, FixedClock.Now.AddMinutes(-2));
        await TestSignIn.SignInAsync(client, fake, expired);

        // Act
        IDocument page = await new FormClient(client).GetPageAsync("/");

        // Assert
        Assert.Equal("Welcome, Alice Rao.", page.QuerySelector("[data-welcome]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-page-alert]"));
        Assert.Contains(expired.RefreshToken, fake.RequestBodies.Single(body => body.Contains("refreshToken")));
        Assert.Equal("Bearer " + renewed.AccessToken, fake.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_AccessTokenExpiresWithinMargin_RenewsFirst()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair renewed = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, RefreshPath, () => IdentityStubs.TokenAnswer(renewed));
        fake.Map(HttpMethod.Get, ProfilePath, () => HomeTests.ProfileAnswer("alice@example.com", "Alice Rao", "customer"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair expiring = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer, FixedClock.Now.AddSeconds(30));
        await TestSignIn.SignInAsync(client, fake, expiring);

        // Act
        await new FormClient(client).GetPageAsync("/");

        // Assert
        Assert.Equal(1, RefreshCalls(fake));
        Assert.Equal("Bearer " + renewed.AccessToken, fake.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_RenewalRefused_EndsSessionAndSendsToSignIn()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, RefreshPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "REFRESH_TOKEN_INVALID"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair expired = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer, FixedClock.Now.AddMinutes(-2));
        await TestSignIn.SignInAsync(client, fake, expired);

        // Act
        HttpResponseMessage response = await client.GetAsync("/");
        HttpResponseMessage signInPage = await client.GetAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login?returnUrl=%2F&reason=session-ended", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, signInPage.StatusCode);
        Assert.Contains("Your session has ended. Please sign in again.", await signInPage.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_ConcurrentRequestsNeedRenewal_RenewsOnce()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair renewed = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, RefreshPath, () => IdentityStubs.TokenAnswer(renewed));
        fake.Map(HttpMethod.Get, ProfilePath, () => HomeTests.ProfileAnswer("alice@example.com", "Alice Rao", "customer"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair expired = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer, FixedClock.Now.AddMinutes(-2));
        await TestSignIn.SignInAsync(client, fake, expired);

        // Act
        Task<HttpResponseMessage> first = client.GetAsync("/");
        Task<HttpResponseMessage> second = client.GetAsync("/");
        HttpResponseMessage[] responses = await Task.WhenAll(first, second);

        // Assert
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, RefreshCalls(fake));
    }

    [Fact]
    public async Task Get_IdentityUnreachableDuringRenewal_KeepsSession()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, RefreshPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair expired = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer, FixedClock.Now.AddMinutes(-2));
        await TestSignIn.SignInAsync(client, fake, expired);

        // Act
        IDocument page = await new FormClient(client).GetPageAsync("/");
        HttpResponseMessage stillSignedIn = await client.GetAsync("/auth/login");

        // Assert
        Assert.Equal("Welcome, alice@example.com.", page.QuerySelector("[data-welcome]")!.TextContent);
        Assert.Equal(HttpStatusCode.Redirect, stillSignedIn.StatusCode);
        Assert.Equal("/", stillSignedIn.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Get_ServiceAnswersUnauthorized_EndsSession()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));

        // Act
        HttpResponseMessage response = await client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login?returnUrl=%2F&reason=session-ended", response.Headers.Location!.OriginalString);
    }

    private static int RefreshCalls(FakeBackend fake)
    {
        return fake.Requests.Count(request => request.RequestUri!.AbsolutePath == RefreshPath);
    }
}
