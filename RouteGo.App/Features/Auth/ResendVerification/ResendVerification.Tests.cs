using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Auth.ResendVerification;

public sealed class ResendVerificationTests
{
    private const string ResendPath = "/auth/resend-verification";
    private const string VerificationEmailsPath = "/auth/verificationEmails";

    [Fact]
    public async Task Get_WithEmail_PrefillsForm()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(ResendPath + "?email=alice%40example.com");

        // Assert
        Assert.Equal("Send the verification email again", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Enter the email you registered with. We will send you a new verification link.", page.QuerySelector("[data-resend-text]")!.TextContent);
        Assert.Equal("alice@example.com", Value(page));
        Assert.Equal("Send verification email", page.QuerySelector("form[data-resend-form] button")!.TextContent);
        Assert.Equal("/auth/login", page.QuerySelector("[data-back-to-sign-in]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Post_Registered_ShowsNewLinkSentWithExpiryInIst()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, VerificationEmailsPath,
            () => FakeBackend.Json(new { email = "alice@example.com", linkExpiresAt = "2026-10-03T10:30:00Z" }, HttpStatusCode.OK));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ResendPath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "alice@example.com");
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("{\"email\":\"alice@example.com\"}", fake.LastRequestBody);
        Assert.Equal(
            "We sent a new verification link to alice@example.com. It works until 3 Oct 2026, 4:00 PM IST. Links in older emails no longer work.",
            result.QuerySelector("[data-sent]")!.TextContent);
        Assert.Equal("Back to sign in", result.QuerySelector("[data-back-to-sign-in]")!.TextContent);
        Assert.Null(result.QuerySelector("form[data-resend-form]"));
    }

    [Fact]
    public async Task Post_EmailNotRegistered_ShowsMessageWithRegisterLinkKeepsEmail()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "EMAIL_NOT_REGISTERED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ResendPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "nobody@example.com");

        // Assert
        Assert.StartsWith("No account uses this email. Check the spelling, or create an account.", AlertText(result));
        Assert.Equal("/auth/register", result.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal("nobody@example.com", Value(result));
    }

    [Theory]
    [InlineData("active", "This email is already verified. You can sign in.")]
    [InlineData("pendingApproval", "This email is already verified. Your operator application is waiting for review.")]
    public async Task Post_EmailAlreadyVerified_ShowsMessageByAccountStatus(string accountStatus, string message)
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["accountStatus"] = accountStatus };
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "EMAIL_ALREADY_VERIFIED", extensions));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ResendPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com");

        // Assert
        Assert.StartsWith(message, AlertText(result));
    }

    [Fact]
    public async Task Post_AskedTooOften_ShowsWaitAndHoldsTheAction()
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["retryAfterSeconds"] = 30 };
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.TooManyRequests, "RATE_LIMITED", extensions));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ResendPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com");

        // Assert
        Assert.Equal("You asked for a new link a short time ago. You can ask again in 30 seconds.", AlertText(result));
        Assert.Equal("30", result.QuerySelector("form[data-resend-form] button")!.GetAttribute("data-wait-seconds"));
    }

    [Fact]
    public async Task Post_AskedTooOftenForHours_ShowsWaitInWords()
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["retryAfterSeconds"] = 70800 };
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.TooManyRequests, "RATE_LIMITED", extensions));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ResendPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com");

        // Assert
        Assert.Equal("You asked for a new link a short time ago. You can ask again in 19 hours 40 minutes.", AlertText(result));
    }

    [Fact]
    public async Task Post_MalformedEmail_ShowsFormatMessageWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ResendPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "not-an-email");

        // Assert
        Assert.Equal("Enter a valid email.", result.QuerySelector("[data-valmsg-for='Input.Email']")!.TextContent.Trim());
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamKeepsEmail()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, VerificationEmailsPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ResendPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com");

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(result));
        Assert.Equal("alice@example.com", Value(result));
    }

    private static FakeBackend RefusingBackend(HttpResponseMessage answer)
    {
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, VerificationEmailsPath, () => answer);
        return fake;
    }

    private static async Task<HttpResponseMessage> SubmitAsync(FormClient form, IDocument page, string email)
    {
        var fields = new Dictionary<string, string> { ["Input.Email"] = email };
        return await form.SendFormAsync(page, "form[data-resend-form]", fields);
    }

    private static async Task<IDocument> SubmitAndReadAsync(FormClient form, IDocument page, string email)
    {
        HttpResponseMessage response = await SubmitAsync(form, page, email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static string? Value(IDocument page)
    {
        return page.QuerySelector("input[name='Input.Email']")!.GetAttribute("value");
    }

    private static string AlertText(IDocument page)
    {
        return page.QuerySelector("[data-page-alert]")!.TextContent.Trim();
    }
}
