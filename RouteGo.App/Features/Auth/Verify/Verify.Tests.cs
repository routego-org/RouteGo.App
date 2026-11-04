using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using RouteGo.App.Features.Home.Home;

namespace RouteGo.App.Features.Auth.Verify;

public sealed class VerifyTests
{
    private const string VerifyPath = "/auth/verify";
    private const string VerificationsPath = "/auth/emailVerifications";

    [Fact]
    public async Task Get_WithToken_OffersVerifyWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(VerifyPath + "?token=abc123");

        // Assert
        Assert.Equal("Verify your email", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Click the button below to verify your email address.", page.QuerySelector("[data-verify-text]")!.TextContent);
        Assert.Equal("Verify my email", page.QuerySelector("form[data-verify-form] button")!.TextContent);
        Assert.Equal("/auth/verify", page.QuerySelector("form[data-verify-form]")!.GetAttribute("action"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_Customer_StartsSessionAndGoesHomeWithWelcome()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair tokens = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, VerificationsPath, () => VerificationAnswer("active", tokens));
        fake.Map(HttpMethod.Get, "/me", () => HomeTests.ProfileAnswer("alice@example.com", "Alice Rao", "customer"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(VerifyPath + "?token=abc123");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, "form[data-verify-form]", TokenField());
        IDocument home = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Equal("{\"token\":\"abc123\"}", fake.RequestBodies[0]);
        Assert.Equal("Your email is verified. Welcome to RouteGo.", home.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal(new[] { "Home", "My bookings", "My profile", "Sign out" }, LinkTexts(home, "[data-role-menu] a, [data-role-menu] button"));
    }

    [Fact]
    public async Task Post_Operator_ShowsReviewMessageWithoutSession()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, VerificationsPath, () => VerificationAnswer("pendingApproval", null));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(VerifyPath + "?token=abc123");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, "form[data-verify-form]", TokenField());
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("Your email is verified. Our team will now review your operator application. We will email you when it is decided.", AlertText(result));
        Assert.Equal("Go to Home", result.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Null(result.QuerySelector("[data-signed-in-as]"));
        Assert.Null(result.QuerySelector("form[data-verify-form]"));
    }

    [Theory]
    [InlineData("VERIFICATION_LINK_EXPIRED", "This verification link has expired.", "Send me a new link", "/auth/resend-verification")]
    [InlineData("VERIFICATION_LINK_INVALID", "This verification link is not valid. It may have been replaced by a newer link. Use the link in the newest email, or ask for a new one.", "Send me a new link", "/auth/resend-verification")]
    public async Task Post_LinkRefused_ShowsMessageWithNewLinkOffer(string code, string message, string linkText, string linkHref)
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, VerificationsPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, code));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(VerifyPath + "?token=abc123");

        // Act
        IDocument result = await SubmitAndReadAsync(form, page);

        // Assert
        Assert.StartsWith(message, AlertText(result));
        IElement link = result.QuerySelector("[data-page-alert] a")!;
        Assert.Equal(linkText, link.TextContent);
        Assert.Equal(linkHref, link.GetAttribute("href"));
        Assert.Null(result.QuerySelector("[data-signed-in-as]"));
    }

    [Theory]
    [InlineData("active", "Your email is already verified. You can sign in.", "Sign in")]
    [InlineData("pendingApproval", "Your email is already verified. Your operator application is waiting for review.", "Go to Home")]
    public async Task Post_LinkAlreadyUsed_ShowsMessageByAccountStatus(string accountStatus, string message, string linkText)
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["accountStatus"] = accountStatus };
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, VerificationsPath,
            () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "VERIFICATION_LINK_ALREADY_USED", extensions));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(VerifyPath + "?token=abc123");

        // Act
        IDocument result = await SubmitAndReadAsync(form, page);

        // Assert
        Assert.StartsWith(message, AlertText(result));
        Assert.Equal(linkText, result.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Null(result.QuerySelector("form[data-verify-form]"));
    }

    [Fact]
    public async Task Get_WithoutToken_ShowsIncompleteWithoutVerifyAction()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(VerifyPath);

        // Assert
        Assert.StartsWith("This verification link is not complete. Open the link from your email again, or ask for a new one.", AlertText(page));
        Assert.Equal("/auth/resend-verification", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("form[data-verify-form]"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamAndKeepsVerifyAction()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, VerificationsPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(VerifyPath + "?token=abc123");

        // Act
        IDocument result = await SubmitAndReadAsync(form, page);

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(result));
        Assert.Equal("abc123", result.QuerySelector("input[name='Input.Token']")!.GetAttribute("value"));
    }

    private static HttpResponseMessage VerificationAnswer(string status, TokenPair? tokens)
    {
        string role = "customer";
        if (tokens is null)
        {
            role = "operator";
        }

        var answer = new { userId = Guid.NewGuid(), role, status, tokens };
        return FakeBackend.Json(answer, HttpStatusCode.OK);
    }

    // The hidden field as the page rendered it (FormClient posts only the fields it is given).
    private static Dictionary<string, string> TokenField()
    {
        return new Dictionary<string, string> { ["Input.Token"] = "abc123" };
    }

    private static async Task<IDocument> SubmitAndReadAsync(FormClient form, IDocument page)
    {
        HttpResponseMessage response = await form.SendFormAsync(page, "form[data-verify-form]", TokenField());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static string AlertText(IDocument page)
    {
        return page.QuerySelector("[data-page-alert]")!.TextContent.Trim();
    }

    private static string[] LinkTexts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(link => link.TextContent.Trim()).ToArray();
    }
}
