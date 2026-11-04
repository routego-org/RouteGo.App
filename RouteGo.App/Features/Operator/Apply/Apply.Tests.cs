using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Operator.Apply;

public sealed class ApplyTests
{
    private const string ApplyPath = "/operator/apply";
    private const string ApplicationsPath = "/operators/applications";
    private const string FormSelector = "form[data-apply-form]";
    private const string SignInEmail = "owner@busco.in";
    private const string CompanyName = "BusCo Travels";
    private const string Gstin = "27AAPFU0939F1ZV";

    [Fact]
    public async Task Get_Visitor_ShowsFormAndReapplyLink()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        var form = new FormClient(TestHost.CreateClient(factory));
        string[] labels = new[] { "Email", "Password", "Full name", "Mobile number (optional)", "Company name", "Contact email", "GSTIN" };

        // Act
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Assert
        Assert.Equal("Apply to sell bus tickets on RouteGo", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(
            "Tell us about your bus company. After you verify your email, our team reviews your application.",
            page.QuerySelector("[data-apply-intro]")!.TextContent);
        Assert.Equal(labels, Labels(page));
        Assert.Equal("You will use this email to sign in.", page.QuerySelector("#email-hint")!.TextContent);
        Assert.Equal("15 characters, for example 27AAPFU0939F1ZV.", page.QuerySelector("#gstin-hint")!.TextContent);
        Assert.Equal("Send application", page.QuerySelector(FormSelector + " button[type=submit]")!.TextContent);
        Assert.Equal("Re-apply", page.QuerySelector("[data-apply-links] a[href='/operator/reapply']")!.TextContent);
    }

    [Fact]
    public async Task Post_NewApplication_ShowsVerifyEmailWithoutSigningIn()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(ApplicationAnswer());
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, SignInEmail, CompanyName, Gstin);
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ApplyPath, response.Headers.Location!.OriginalString);
        Assert.Equal(
            "{\"email\":\"owner@busco.in\",\"password\":\"travel2026x\",\"fullName\":\"Ravi Kumar\",\"phone\":null,"
            + "\"companyName\":\"BusCo Travels\",\"contactEmail\":\"bookings@busco.in\",\"gstin\":\"27AAPFU0939F1ZV\"}",
            fake.LastRequestBody);
        Assert.Equal(
            "Thank you for applying. We sent a verification link to owner@busco.in. Open the email and click the link. After you verify your email, our team will review your application and email you the result.",
            result.QuerySelector("[data-applied] p")!.TextContent);
        IElement resend = result.QuerySelector("[data-applied] a")!;
        Assert.Equal("Send the email again", resend.TextContent);
        Assert.Equal("/auth/resend-verification?email=owner%40busco.in", resend.GetAttribute("href"));
        Assert.Null(result.QuerySelector(FormSelector));
        Assert.Null(result.QuerySelector("[data-signed-in-as]"));
    }

    [Fact]
    public async Task Post_GstinTypedInLowerCaseWithSpaces_SendsTidiedGstin()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(ApplicationAnswer());
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, SignInEmail, CompanyName, " 27aapfu0939f1zv ");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("\"gstin\":\"27AAPFU0939F1ZV\"", fake.LastRequestBody);
    }

    [Fact]
    public async Task Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, SignInEmail, "B", " 27aapfu0939f1z");

        // Assert
        Assert.Equal("Enter a valid 15-character GSTIN, for example 27AAPFU0939F1ZV.", FieldError(result, "Input.Gstin"));
        Assert.Equal("Company name must be at least 2 characters.", FieldError(result, "Input.CompanyName"));
        Assert.Equal("27AAPFU0939F1Z", Value(result, "Input.Gstin"));
        Assert.Equal("B", Value(result, "Input.CompanyName"));
        Assert.Null(Value(result, "Input.Password"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_EmailLongerThan254Characters_ShowsFormatMessageWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);
        string longEmail = new string('a', 246) + "@busco.in";

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, longEmail, CompanyName, Gstin);

        // Assert
        Assert.Equal("Enter a valid email.", FieldError(result, "Input.Email"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_EmailAlreadyRegistered_ShowsMessageWithReapplyAndSignInLinks()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.Conflict, "EMAIL_ALREADY_REGISTERED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, SignInEmail, CompanyName, Gstin);

        // Assert
        Assert.StartsWith(
            "This email is already registered. If it is your operator account and your application was rejected, re-apply instead. Otherwise, sign in.",
            AlertText(result));
        Assert.Equal(new[] { "Re-apply", "Sign in" }, AlertLinkTexts(result));
        Assert.Equal(new[] { "/operator/reapply", "/auth/login" }, AlertLinkHrefs(result));
        Assert.Equal(SignInEmail, Value(result, "Input.Email"));
        Assert.Equal(CompanyName, Value(result, "Input.CompanyName"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_GstinAlreadyRegistered_ShowsMessageOnGstinField()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.Conflict, "GSTIN_ALREADY_REGISTERED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, SignInEmail, CompanyName, Gstin);

        // Assert
        Assert.Equal(
            "This GSTIN is already registered with RouteGo. If you think this is wrong, contact RouteGo support.",
            FieldError(result, "Input.Gstin"));
        Assert.Null(result.QuerySelector("[data-page-alert]"));
        Assert.Equal(Gstin, Value(result, "Input.Gstin"));
        Assert.Equal(SignInEmail, Value(result, "Input.Email"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, ApplicationsPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, SignInEmail, CompanyName, Gstin);

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(result));
        Assert.Equal(SignInEmail, Value(result, "Input.Email"));
        Assert.Equal(Gstin, Value(result, "Input.Gstin"));
        Assert.Null(Value(result, "Input.Password"));
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Post_IdentityFieldError_ShowsFieldMessageKeepsValues()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.ValidationProblem("contactEmail", "'Contact Email' is not a valid email address."));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, SignInEmail, CompanyName, Gstin);

        // Assert
        Assert.Equal("Enter a valid contact email.", FieldError(result, "Input.ContactEmail"));
        Assert.Equal("bookings@busco.in", Value(result, "Input.ContactEmail"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_UnknownProblem_ShowsInternalError()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ApplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, SignInEmail, CompanyName, Gstin);

        // Assert
        Assert.Equal(
            "Something went wrong on our side. Try again — if it keeps happening, contact support with reference trace-internal_error.",
            AlertText(result));
        Assert.Null(Value(result, "Input.Password"));
    }

    /// <summary>Identity's 201 OperatorApplication answer for BusCo.</summary>
    private static HttpResponseMessage ApplicationAnswer()
    {
        var application = new
        {
            applicationId = Guid.NewGuid(),
            operatorId = Guid.NewGuid(),
            email = SignInEmail,
            fullName = "Ravi Kumar",
            companyName = CompanyName,
            contactEmail = "bookings@busco.in",
            gstin = Gstin,
            status = "pendingVerification",
            submittedAt = "2026-10-08T10:00:00Z",
            reviewedAt = (string?)null,
            rejectReason = (string?)null,
            previousRejections = Array.Empty<object>(),
        };
        return FakeBackend.Json(application, HttpStatusCode.Created);
    }

    private static FakeBackend AnsweringBackend(HttpResponseMessage answer)
    {
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, ApplicationsPath, () => answer);
        return fake;
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        FormClient form, IDocument page, string email, string companyName, string gstin)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "travel2026x",
            ["Input.FullName"] = "Ravi Kumar",
            ["Input.Phone"] = "",
            ["Input.CompanyName"] = companyName,
            ["Input.ContactEmail"] = "bookings@busco.in",
            ["Input.Gstin"] = gstin,
        };
        return await form.SendFormAsync(page, FormSelector, fields);
    }

    private static async Task<IDocument> SubmitAndReadAsync(
        FormClient form, IDocument page, string email, string companyName, string gstin)
    {
        HttpResponseMessage response = await SubmitAsync(form, page, email, companyName, gstin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static string[] Labels(IDocument page)
    {
        return page.QuerySelectorAll(FormSelector + " label").Select(label => label.TextContent.Trim()).ToArray();
    }

    private static string[] AlertLinkTexts(IDocument page)
    {
        return page.QuerySelectorAll("[data-page-alert] a").Select(link => link.TextContent).ToArray();
    }

    private static string[] AlertLinkHrefs(IDocument page)
    {
        return page.QuerySelectorAll("[data-page-alert] a").Select(link => link.GetAttribute("href")!).ToArray();
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
