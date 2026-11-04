using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Auth.Register;

public sealed class RegisterTests
{
    private const string RegisterPath = "/auth/register";
    private const string RegistrationsPath = "/auth/registrations";

    [Fact]
    public async Task Get_Visitor_ShowsFormAndLinks()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Assert
        Assert.Equal("Create your account", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(new[] { "Email", "Password", "Full name", "Mobile number (optional)" }, Labels(page));
        Assert.Equal("At least 10 characters, with at least one letter and one number.", page.QuerySelector("[data-password-hint]")!.TextContent);
        Assert.Equal("Create account", page.QuerySelector("form[data-register-form] button[type=submit]")!.TextContent);
        Assert.Equal("Sign in", page.QuerySelector("[data-register-links] a[href='/auth/login']")!.TextContent);
        Assert.Equal("Apply as a bus operator", page.QuerySelector("[data-register-links] a[href='/operator/apply']")!.TextContent);
    }

    [Fact]
    public async Task Post_NewEmail_ShowsCheckYourEmailWithoutSigningIn()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, RegistrationsPath, () => SummaryAnswer("alice@example.com"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        var form = new FormClient(client);
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "travel2026x", "9876543210");
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RegisterPath, response.Headers.Location!.OriginalString);
        Assert.Equal(
            "{\"email\":\"alice@example.com\",\"password\":\"travel2026x\",\"fullName\":\"Alice Rao\",\"phone\":\"9876543210\"}",
            fake.LastRequestBody);
        Assert.Equal(
            "Your account is created. We sent a verification link to alice@example.com. Open the email and click the link to activate your account. The link works for 24 hours.",
            result.QuerySelector("[data-registered] p")!.TextContent);
        IElement resend = result.QuerySelector("[data-registered] a")!;
        Assert.Equal("Send the email again", resend.TextContent);
        Assert.Equal("/auth/resend-verification?email=alice%40example.com", resend.GetAttribute("href"));
        Assert.Null(result.QuerySelector("form[data-register-form]"));
        Assert.Null(result.QuerySelector("[data-signed-in-as]"));
    }

    [Fact]
    public async Task Post_NoMobileNumber_SendsNoPhone()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, RegistrationsPath, () => SummaryAnswer("alice@example.com"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "travel2026x", "");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("\"phone\":null", fake.LastRequestBody);
    }

    [Fact]
    public async Task Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "short1", "5876543210");

        // Assert
        Assert.Equal("Password must be at least 10 characters and have at least one letter and one number.", FieldError(result, "Input.Password"));
        Assert.Equal("Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9.", FieldError(result, "Input.Phone"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_EmailAlreadyRegistered_ShowsMessageKeepsValuesEmptiesPassword()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.Conflict, "EMAIL_ALREADY_REGISTERED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "travel2026x", "9876543210");

        // Assert
        Assert.StartsWith("This email is already registered. Sign in instead.", AlertText(result));
        Assert.Equal("/auth/login", result.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal("alice@example.com", Value(result, "Input.Email"));
        Assert.Equal("Alice Rao", Value(result, "Input.FullName"));
        Assert.Equal("9876543210", Value(result, "Input.Phone"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_IdentityFieldError_ShowsFieldMessageKeepsValues()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.ValidationProblem("fullName", "Full name contains characters that are not allowed."));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "travel2026x", "9876543210");

        // Assert
        Assert.Equal("Full name contains characters that are not allowed.", FieldError(result, "Input.FullName"));
        Assert.Equal("Alice Rao", Value(result, "Input.FullName"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, RegistrationsPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "travel2026x", "9876543210");

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(result));
        Assert.Equal("alice@example.com", Value(result, "Input.Email"));
        Assert.Null(Value(result, "Input.Password"));
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Post_UnknownProblem_ShowsInternalError()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(RegisterPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "travel2026x", "9876543210");

        // Assert
        Assert.Equal(
            "Something went wrong on our side. Try again — if it keeps happening, contact support with reference trace-internal_error.",
            AlertText(result));
    }

    private static HttpResponseMessage SummaryAnswer(string email)
    {
        var summary = new
        {
            userId = Guid.NewGuid(),
            email,
            fullName = "Alice Rao",
            phone = "+919876543210",
            role = "customer",
            status = "pendingVerification",
            createdAt = "2026-10-02T10:00:00Z",
        };
        return FakeBackend.Json(summary, HttpStatusCode.Created);
    }

    private static FakeBackend RefusingBackend(HttpResponseMessage answer)
    {
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, RegistrationsPath, () => answer);
        return fake;
    }

    private static async Task<HttpResponseMessage> SubmitAsync(FormClient form, IDocument page, string password, string phone)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = "alice@example.com",
            ["Input.Password"] = password,
            ["Input.FullName"] = "Alice Rao",
            ["Input.Phone"] = phone,
        };
        return await form.SendFormAsync(page, "form[data-register-form]", fields);
    }

    private static async Task<IDocument> SubmitAndReadAsync(FormClient form, IDocument page, string password, string phone)
    {
        HttpResponseMessage response = await SubmitAsync(form, page, password, phone);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static string[] Labels(IDocument page)
    {
        return page.QuerySelectorAll("form[data-register-form] label").Select(label => label.TextContent.Trim()).ToArray();
    }

    private static string? Value(IDocument page, string field)
    {
        return page.QuerySelector($"input[name='{field}']")!.GetAttribute("value");
    }

    private static string AlertText(IDocument page)
    {
        return page.QuerySelector("[data-page-alert]")!.TextContent.Trim();
    }

    private static string FieldError(IDocument page, string field)
    {
        return page.QuerySelector($"[data-valmsg-for='{field}']")!.TextContent.Trim();
    }
}
