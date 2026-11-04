using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Auth.Login;

public sealed class LoginTests
{
    private const string LoginPath = "/auth/login";
    private const string SessionsPath = "/auth/sessions";

    [Fact]
    public async Task Get_Visitor_ShowsFormAndLinks()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(LoginPath);

        // Assert
        Assert.Equal("Sign in", page.QuerySelector("h1")!.TextContent);
        Assert.NotNull(page.QuerySelector("input[name='Input.Email']"));
        Assert.NotNull(page.QuerySelector("input[name='Input.Password'][type=password]"));
        Assert.Equal("Sign in", page.QuerySelector("form[data-login-form] button[type=submit]")!.TextContent);
        Assert.Equal("Create an account", page.QuerySelector("[data-login-links] a[href='/auth/register']")!.TextContent);
        Assert.Equal("Didn't get the verification email?", page.QuerySelector("[data-login-links] a[href='/auth/resend-verification']")!.TextContent);
        Assert.Equal("Apply as a bus operator", page.QuerySelector("[data-login-links] a[href='/operator/apply']")!.TextContent);
        Assert.Null(page.QuerySelector("[data-page-alert]"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_RightPassword_StartsSessionAndGoesHome()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair tokens = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, SessionsPath, () => IdentityStubs.TokenAnswer(tokens));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        var form = new FormClient(client);
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.Equal("{\"email\":\"alice@example.com\",\"password\":\"travel2026x\"}", fake.LastRequestBody);
        string sessionCookie = SessionCookie(response);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_RightPasswordWithReturnUrl_GoesToReturnUrl()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair tokens = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, SessionsPath, () => IdentityStubs.TokenAnswer(tokens));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath + "?returnUrl=%2Fprofile");

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/profile", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_ReturnUrlOnAnotherSite_GoesHome()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair tokens = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, SessionsPath, () => IdentityStubs.TokenAnswer(tokens));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath + "?returnUrl=https%3A%2F%2Fevil.example%2Fsteal");

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Get_SignedIn_GoesHome()
    {
        // Arrange
        var fake = new FakeBackend();
        TokenPair tokens = IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer);
        fake.Map(HttpMethod.Post, SessionsPath, () => IdentityStubs.TokenAnswer(tokens));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        var form = new FormClient(client);
        IDocument page = await form.GetPageAsync(LoginPath);
        await SubmitAsync(form, page, "alice@example.com", "travel2026x");

        // Act
        HttpResponseMessage response = await client.GetAsync(LoginPath);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_InvalidCredentials_ShowsMessageKeepsEmailEmptiesPassword()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com", "wrong-password1");

        // Assert
        Assert.Equal("The email or password is incorrect.", AlertText(result));
        Assert.Equal("alice@example.com", result.QuerySelector("input[name='Input.Email']")!.GetAttribute("value"));
        Assert.Null(result.QuerySelector("input[name='Input.Password']")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Post_EmailNotVerified_ShowsMessageWithResendLink()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "new@example.com", "travel2026x");

        // Assert
        Assert.StartsWith("Please verify your email before signing in. Check your inbox for the verification link.", AlertText(result));
        IElement link = result.QuerySelector("[data-page-alert] a")!;
        Assert.Equal("Send the link again", link.TextContent);
        Assert.Equal("/auth/resend-verification?email=new%40example.com", link.GetAttribute("href"));
    }

    [Fact]
    public async Task Post_AccountPendingApproval_ShowsWaitingMessage()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.Forbidden, "ACCOUNT_PENDING_APPROVAL"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "owner@busco.example", "travel2026x");

        // Assert
        Assert.Equal("Your operator application is waiting for review. We will email you when it is decided.", AlertText(result));
    }

    [Fact]
    public async Task Post_AccountRejected_ShowsMessageWithReapplyLink()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.Forbidden, "ACCOUNT_REJECTED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "owner@busco.example", "travel2026x");

        // Assert
        Assert.StartsWith("Your operator application was not approved. The email we sent you gives the reason. You can apply again 7 days after the rejection.", AlertText(result));
        IElement link = result.QuerySelector("[data-page-alert] a")!;
        Assert.Equal("Re-apply", link.TextContent);
        Assert.Equal("/operator/reapply", link.GetAttribute("href"));
    }

    [Fact]
    public async Task Post_AccountSuspended_ShowsSuspendedMessage()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal("Your account is suspended. Please contact RouteGo support.", AlertText(result));
    }

    [Fact]
    public async Task Post_AccountLocked_ShowsLockEndInIst()
    {
        // Arrange
        var lockedUntil = new Dictionary<string, object> { ["lockedUntil"] = "2026-10-02T05:15:00Z" };
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.Forbidden, "ACCOUNT_LOCKED", lockedUntil));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal(
            "Your account is locked for a short time because of too many wrong passwords. Try again after 2 Oct 2026, 10:45 AM IST.",
            AlertText(result));
    }

    [Fact]
    public async Task Get_SessionEnded_ShowsSessionEndedMessage()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(LoginPath + "?returnUrl=%2Fprofile&reason=session-ended");

        // Assert
        Assert.Equal("Your session has ended. Please sign in again.", AlertText(page));
    }

    [Fact]
    public async Task Get_SignInRequired_ShowsSignInRequiredMessage()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(LoginPath + "?returnUrl=%2Fprofile&reason=sign-in-required");

        // Assert
        Assert.Equal("Please sign in to continue.", AlertText(page));
    }

    [Fact]
    public async Task Post_MalformedInput_ShowsFieldMessagesWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "not-an-email", "");

        // Assert
        Assert.Equal("Enter a valid email.", FieldError(result, "Input.Email"));
        Assert.Equal("Password is required.", FieldError(result, "Input.Password"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_IdentityFieldError_ShowsFieldMessage()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.ValidationProblem("email", "The email is not valid."));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal("Enter a valid email.", FieldError(result, "Input.Email"));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamMessageKeepsEmail()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, SessionsPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(result));
        Assert.Equal("alice@example.com", result.QuerySelector("input[name='Input.Email']")!.GetAttribute("value"));
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Post_UnknownProblem_ShowsInternalErrorWithTraceId()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(LoginPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "alice@example.com", "travel2026x");

        // Assert
        Assert.Equal(
            "Something went wrong on our side. Try again — if it keeps happening, contact support with reference trace-internal_error.",
            AlertText(result));
    }

    private static FakeBackend RefusingBackend(HttpResponseMessage answer)
    {
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, SessionsPath, () => answer);
        return fake;
    }

    private static async Task<HttpResponseMessage> SubmitAsync(FormClient form, IDocument page, string email, string password)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        };
        return await form.SendFormAsync(page, "form[data-login-form]", fields);
    }

    private static async Task<IDocument> SubmitAndReadAsync(FormClient form, IDocument page, string email, string password)
    {
        HttpResponseMessage response = await SubmitAsync(form, page, email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static string AlertText(IDocument page)
    {
        return page.QuerySelector("[data-page-alert]")!.TextContent.Trim();
    }

    private static string FieldError(IDocument page, string field)
    {
        return page.QuerySelector($"[data-valmsg-for='{field}']")!.TextContent.Trim();
    }

    private static string SessionCookie(HttpResponseMessage response)
    {
        IEnumerable<string> cookies = response.Headers.GetValues("Set-Cookie");
        return cookies.First(cookie => cookie.StartsWith("routego.session", StringComparison.Ordinal));
    }
}
