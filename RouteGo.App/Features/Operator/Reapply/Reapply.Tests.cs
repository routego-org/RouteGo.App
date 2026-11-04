using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Operator.Reapply;

public sealed class ReapplyTests
{
    private const string ReapplyPath = "/operator/reapply";
    private const string ReapplicationsPath = "/operators/reapplications";
    private const string FormSelector = "form[data-reapply-form]";
    private const string SignInEmail = "owner@busco.in";
    private const string CompanyName = "BusCo Travels";
    private const string Gstin = "27AAPFU0939F1ZV";

    [Fact]
    public async Task Get_Visitor_ShowsFormAndApplyLink()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        var form = new FormClient(TestHost.CreateClient(factory));

        // Act
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Assert
        Assert.Equal("Re-apply as a bus operator", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(
            "Was your application not approved? Enter your sign-in email and password, and your corrected company details. You can re-apply 7 days after the rejection.",
            page.QuerySelector("[data-reapply-intro]")!.TextContent);
        Assert.Equal(new[] { "Email", "Password", "Company name", "Contact email", "GSTIN" }, Labels(page));
        Assert.Equal("15 characters, for example 27AAPFU0939F1ZV.", page.QuerySelector("#gstin-hint")!.TextContent);
        Assert.Equal("Send new application", page.QuerySelector(FormSelector + " button[type=submit]")!.TextContent);
        Assert.Equal("Apply as a bus operator", page.QuerySelector("[data-reapply-links] a[href='/operator/apply']")!.TextContent);
    }

    [Fact]
    public async Task Post_RejectedLongEnoughAgo_ShowsSentWithEarlierRejections()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(ApplicationAnswer());
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, CompanyName, " 27aapfu0939f1zv ");
        string? sentBody = fake.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ReapplyPath, response.Headers.Location!.OriginalString);
        Assert.Equal(
            "{\"email\":\"owner@busco.in\",\"password\":\"travel2026x\",\"companyName\":\"BusCo Travels\","
            + "\"contactEmail\":\"bookings@busco.in\",\"gstin\":\"27AAPFU0939F1ZV\"}",
            sentBody);
        Assert.Equal(
            "Your new application is sent. Our team will review it and email you the result.",
            result.QuerySelector("[data-reapplied] p")!.TextContent);
        Assert.Equal(
            new[] { "Earlier rejection: GSTIN does not match company name", "Earlier rejection: Company name missing" },
            Texts(result, "[data-rejection-reason]"));
        Assert.Equal(new[] { "Rejected on 30 Sep 2026", "Rejected on 3 Sep 2026" }, Texts(result, "[data-rejection-date]"));
        Assert.Equal("/", result.QuerySelector("[data-go-home]")!.GetAttribute("href"));
        Assert.Null(result.QuerySelector(FormSelector));
        Assert.Null(result.QuerySelector("[data-signed-in-as]"));
    }

    [Fact]
    public async Task Post_TooSoon_ShowsAllowedDateInIstKeepsValues()
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["reapplyAllowedAt"] = "2026-10-08T03:30:00Z" };
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "REAPPLICATION_TOO_SOON", extensions));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal("You can re-apply from 8 Oct 2026, 9:00 AM IST.", AlertText(result));
        Assert.Equal(SignInEmail, Value(result, "Input.Email"));
        Assert.Equal(CompanyName, Value(result, "Input.CompanyName"));
        Assert.Equal(Gstin, Value(result, "Input.Gstin"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_WrongEmailOrPassword_ShowsInvalidCredentialsEmptiesPassword()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal("The email or password is incorrect.", AlertText(result));
        Assert.Null(Value(result, "Input.Password"));
        Assert.Equal(SignInEmail, Value(result, "Input.Email"));
    }

    [Fact]
    public async Task Post_AccountLocked_ShowsUnlockTimeInIst()
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["lockedUntil"] = "2026-10-08T05:00:00Z" };
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.Forbidden, "ACCOUNT_LOCKED", extensions));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal(
            "Your account is locked for a short time because of too many wrong passwords. Try again after 8 Oct 2026, 10:30 AM IST.",
            AlertText(result));
    }

    [Theory]
    [InlineData("pendingApproval", "Your application is already waiting for review. We will email you when it is decided.", null)]
    [InlineData("pendingVerification", "Please verify your email first. Check your inbox for the verification link. Send the link again", "/auth/resend-verification?email=owner%40busco.in")]
    [InlineData("active", "This account can already sign in. There is nothing to re-apply for. Sign in", "/auth/login")]
    [InlineData("suspended", "Your account is suspended. Please contact RouteGo support.", null)]
    public async Task Post_OperatorNotRejected_ShowsMessageForCurrentStatus(string currentStatus, string expectedText, string? expectedHref)
    {
        // Arrange
        var extensions = new Dictionary<string, object> { ["currentStatus"] = currentStatus };
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.Conflict, "OPERATOR_NOT_REJECTED", extensions));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal(expectedText, NormalizedAlertText(result));
        Assert.Equal(expectedHref, result.QuerySelector("[data-page-alert] a")?.GetAttribute("href"));
    }

    [Fact]
    public async Task Post_GstinAlreadyRegistered_ShowsMessageOnGstinField()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.Conflict, "GSTIN_ALREADY_REGISTERED"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal(
            "This GSTIN is already registered with RouteGo. If you think this is wrong, contact RouteGo support.",
            FieldError(result, "Input.Gstin"));
        Assert.Null(result.QuerySelector("[data-page-alert]"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "B", "27aapfu0939f1z");

        // Assert
        Assert.Equal("Company name must be at least 2 characters.", FieldError(result, "Input.CompanyName"));
        Assert.Equal("Enter a valid 15-character GSTIN, for example 27AAPFU0939F1ZV.", FieldError(result, "Input.Gstin"));
        Assert.Equal("27AAPFU0939F1Z", Value(result, "Input.Gstin"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Post_IdentityFieldError_ShowsFieldMessageKeepsValues()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.ValidationProblem("contactEmail", "'Contact Email' is not a valid email address."));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal("Enter a valid contact email.", FieldError(result, "Input.ContactEmail"));
        Assert.Equal("bookings@busco.in", Value(result, "Input.ContactEmail"));
        Assert.Null(Value(result, "Input.Password"));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, ReapplicationsPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(result));
        Assert.Equal(CompanyName, Value(result, "Input.CompanyName"));
        Assert.Null(Value(result, "Input.Password"));
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Post_UnknownProblem_ShowsInternalError()
    {
        // Arrange
        FakeBackend fake = AnsweringBackend(IdentityStubs.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR"));
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        var form = new FormClient(TestHost.CreateClient(factory));
        IDocument page = await form.GetPageAsync(ReapplyPath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, CompanyName, Gstin);

        // Assert
        Assert.Equal(
            "Something went wrong on our side. Try again — if it keeps happening, contact support with reference trace-internal_error.",
            AlertText(result));
        Assert.Null(Value(result, "Input.Password"));
    }

    /// <summary>Identity's 201 OperatorApplication answer, listing two earlier rejections, newest first.</summary>
    private static HttpResponseMessage ApplicationAnswer()
    {
        var previousRejections = new[]
        {
            new { applicationId = Guid.NewGuid(), submittedAt = "2026-09-25T10:00:00Z", rejectedAt = "2026-09-30T05:00:00Z", reason = "GSTIN does not match company name" },
            new { applicationId = Guid.NewGuid(), submittedAt = "2026-09-01T10:00:00Z", rejectedAt = "2026-09-02T20:00:00Z", reason = "Company name missing" },
        };
        var application = new
        {
            applicationId = Guid.NewGuid(),
            operatorId = Guid.NewGuid(),
            email = SignInEmail,
            fullName = "Ravi Kumar",
            companyName = CompanyName,
            contactEmail = "bookings@busco.in",
            gstin = Gstin,
            status = "pendingApproval",
            submittedAt = "2026-10-08T10:00:00Z",
            reviewedAt = (string?)null,
            rejectReason = (string?)null,
            previousRejections,
        };
        return FakeBackend.Json(application, HttpStatusCode.Created);
    }

    private static FakeBackend AnsweringBackend(HttpResponseMessage answer)
    {
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, ReapplicationsPath, () => answer);
        return fake;
    }

    private static async Task<HttpResponseMessage> SubmitAsync(FormClient form, IDocument page, string companyName, string gstin)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = SignInEmail,
            ["Input.Password"] = "travel2026x",
            ["Input.CompanyName"] = companyName,
            ["Input.ContactEmail"] = "bookings@busco.in",
            ["Input.Gstin"] = gstin,
        };
        return await form.SendFormAsync(page, FormSelector, fields);
    }

    private static async Task<IDocument> SubmitAndReadAsync(FormClient form, IDocument page, string companyName, string gstin)
    {
        HttpResponseMessage response = await SubmitAsync(form, page, companyName, gstin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static string[] Labels(IDocument page)
    {
        return Texts(page, FormSelector + " label");
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    private static string? Value(IDocument page, string field)
    {
        return page.QuerySelector($"input[name='{field}']")!.GetAttribute("value");
    }

    private static string AlertText(IDocument page)
    {
        return page.QuerySelector("[data-page-alert]")!.TextContent.Trim();
    }

    // The alert's words and link text with the markup's line breaks folded into single spaces.
    private static string NormalizedAlertText(IDocument page)
    {
        string[] words = AlertText(page).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words);
    }

    private static string FieldError(IDocument page, string field)
    {
        return page.QuerySelector($"[data-valmsg-for='{field}']")!.TextContent.Trim();
    }
}
