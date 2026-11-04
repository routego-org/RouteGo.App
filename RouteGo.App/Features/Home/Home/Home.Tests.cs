using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Home.Home;

public sealed class HomeTests
{
    private const string ProfilePath = "/me";

    [Fact]
    public async Task Get_Visitor_ShowsWaysInWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync("/");

        // Assert
        Assert.Equal("Welcome to RouteGo", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Book intercity bus tickets in minutes.", page.QuerySelector("[data-visitor-home] p")!.TextContent);
        Assert.Equal(new[] { "Sign in", "Create an account", "Apply as a bus operator" }, LinkTexts(page, "[data-home-actions] a"));
        Assert.Equal(new[] { "Sign in", "Register", "Apply as operator" }, LinkTexts(page, "[data-role-menu] a, [data-role-menu] button"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Get_Customer_ShowsWelcomeAndCustomerMenu()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("alice@example.com", "Alice Rao", "customer"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));

        // Act
        IDocument page = await new FormClient(client).GetPageAsync("/");

        // Assert
        Assert.Equal("Welcome, Alice Rao.", page.QuerySelector("[data-welcome]")!.TextContent);
        Assert.Equal("You are signed in as Customer.", page.QuerySelector("[data-signed-in-role]")!.TextContent);
        Assert.Equal("Signed in as alice@example.com", page.QuerySelector("[data-signed-in-as]")!.TextContent);
        Assert.Equal(new[] { "Home", "My bookings", "My profile", "Sign out" }, LinkTexts(page, "[data-role-menu] a, [data-role-menu] button"));
    }

    [Fact]
    public async Task Get_Operator_ShowsWelcomeAndOperatorMenu()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("ravi@busco.example", "Ravi Kumar", "operator"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("ravi@busco.example", PortalSession.RoleOperator));
        string[] operatorMenu = new[] { "Home", "My profile", "Dashboard", "Buses", "Routes", "Schedules", "Trips", "Sign out" };

        // Act
        IDocument page = await new FormClient(client).GetPageAsync("/");

        // Assert
        Assert.Equal("Welcome, Ravi Kumar.", page.QuerySelector("[data-welcome]")!.TextContent);
        Assert.Equal("You are signed in as Operator.", page.QuerySelector("[data-signed-in-role]")!.TextContent);
        Assert.Equal("Signed in as ravi@busco.example", page.QuerySelector("[data-signed-in-as]")!.TextContent);
        Assert.Equal(operatorMenu, LinkTexts(page, "[data-role-menu] a, [data-role-menu] button"));
    }

    [Fact]
    public async Task Get_Admin_ShowsWelcomeAndAdminMenu()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("priya@example.com", "Priya Shah", "admin"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("priya@example.com", PortalSession.RoleAdmin));
        string[] adminMenu =
            new[] { "Home", "My profile", "Users", "Create admin", "Operator applications", "Operators", "Trips", "Bookings", "Summary", "Settlements", "Audit log", "Email log", "Signing keys", "Sign out" };

        // Act
        IDocument page = await new FormClient(client).GetPageAsync("/");

        // Assert
        Assert.Equal("Welcome, Priya Shah.", page.QuerySelector("[data-welcome]")!.TextContent);
        Assert.Equal("You are signed in as Admin.", page.QuerySelector("[data-signed-in-role]")!.TextContent);
        Assert.Equal(adminMenu, LinkTexts(page, "[data-role-menu] a, [data-role-menu] button"));
    }

    [Fact]
    public async Task Get_ProfileUnavailable_WelcomesByEmailWithoutError()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));

        // Act
        IDocument page = await new FormClient(client).GetPageAsync("/");

        // Assert
        Assert.Equal("Welcome, alice@example.com.", page.QuerySelector("[data-welcome]")!.TextContent);
        Assert.Equal("You are signed in as Customer.", page.QuerySelector("[data-signed-in-role]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-page-alert]"));
        Assert.Equal(new[] { "Home", "My bookings", "My profile", "Sign out" }, LinkTexts(page, "[data-role-menu] a, [data-role-menu] button"));
    }

    [Fact]
    public async Task Get_ProfileCall_SendsAccessTokenAndCorrelationId()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("alice@example.com", "Alice Rao", "customer"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        TokenPair tokens = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        await TestSignIn.SignInAsync(client, fake, tokens);

        // Act
        await new FormClient(client).GetPageAsync("/");

        // Assert
        Assert.Equal("Bearer " + tokens.AccessToken, fake.LastRequestHeader("Authorization"));
        Assert.False(string.IsNullOrEmpty(fake.LastRequestHeader(CorrelationId.HeaderName)));
    }

    /// <summary>Identity's Profile answer for getMyProfile.</summary>
    public static HttpResponseMessage ProfileAnswer(string email, string fullName, string role)
    {
        var profile = new { userId = Guid.NewGuid(), email, fullName, phone = (string?)null, role };
        return FakeBackend.Json(profile, HttpStatusCode.OK);
    }

    private static string[] LinkTexts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(link => link.TextContent.Trim()).ToArray();
    }
}
