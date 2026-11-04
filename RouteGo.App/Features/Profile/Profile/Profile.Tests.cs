using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Profile.Profile;

public sealed class ProfileTests
{
    private const string ProfilePagePath = "/profile";
    private const string ProfilePath = "/me";
    private const string FormSelector = "form[data-profile-form]";

    [Fact]
    public async Task Get_SignedIn_ShowsProfileWithFixedEmailAndRole()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("Alice Rao", "+919876543210"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);

        // Act
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Assert
        Assert.Equal("My profile", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("alice@example.com", page.QuerySelector("[data-profile-email]")!.TextContent);
        Assert.Equal("Customer", page.QuerySelector("[data-profile-role]")!.TextContent);
        Assert.Equal("Email and role cannot be changed.", page.QuerySelector("[data-profile-hint]")!.TextContent);
        Assert.Null(page.QuerySelector("input[name='Input.Email']"));
        Assert.Equal(new[] { "Full name", "Mobile number (optional)" }, Labels(page));
        Assert.Equal("Alice Rao", Value(page, "Input.FullName"));
        Assert.Equal("+919876543210", Value(page, "Input.Phone"));
        Assert.Equal("Save changes", page.QuerySelector(FormSelector + " button[type=submit]")!.TextContent);
    }

    [Fact]
    public async Task Post_ChangedValues_SavesAndShowsReturnedValues()
    {
        // Arrange
        var fake = new FakeBackend();
        var identity = new SavedProfile("Alice Rao", "+919876543210");
        fake.Map(HttpMethod.Get, ProfilePath, identity.Answer);
        fake.Map(HttpMethod.Put, ProfilePath, () => identity.Replace("Alice R. Rao", "+918765432109"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "Alice R. Rao", "8765432109");
        string? sentBody = fake.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ProfilePagePath, response.Headers.Location!.OriginalString);
        Assert.Equal("{\"fullName\":\"Alice R. Rao\",\"phone\":\"8765432109\"}", sentBody);
        Assert.Equal("Profile updated.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Alice R. Rao", Value(result, "Input.FullName"));
        Assert.Equal("+918765432109", Value(result, "Input.Phone"));
    }

    [Fact]
    public async Task Post_EmptiedMobileNumber_SendsNoPhoneAndShowsEmptyField()
    {
        // Arrange
        var fake = new FakeBackend();
        var identity = new SavedProfile("Alice Rao", "+919876543210");
        fake.Map(HttpMethod.Get, ProfilePath, identity.Answer);
        fake.Map(HttpMethod.Put, ProfilePath, () => identity.Replace("Alice Rao", null));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "Alice Rao", "");
        string? sentBody = fake.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("{\"fullName\":\"Alice Rao\",\"phone\":null}", sentBody);
        Assert.Equal("", Value(result, "Input.Phone"));
    }

    [Fact]
    public async Task Post_FieldRulesBroken_ShowsMessagesWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("Alice Rao", "+919876543210"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "A", "12345");

        // Assert
        Assert.Equal("Full name must be at least 2 characters.", FieldError(result, "Input.FullName"));
        Assert.Equal("Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9.", FieldError(result, "Input.Phone"));
        Assert.Equal("A", Value(result, "Input.FullName"));
        Assert.Equal("12345", Value(result, "Input.Phone"));
        Assert.Equal("alice@example.com", result.QuerySelector("[data-profile-email]")!.TextContent);
        Assert.Equal("Customer", result.QuerySelector("[data-profile-role]")!.TextContent);
        Assert.DoesNotContain(fake.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Post_FullNameOf100Emoji_IsCountedLikeIdentityAndSent()
    {
        // Arrange
        var fake = new FakeBackend();
        var identity = new SavedProfile("Alice Rao", null);
        IEnumerable<string> hundredEmoji = Enumerable.Repeat("😀", 100);
        string emojiName = string.Concat(hundredEmoji);
        fake.Map(HttpMethod.Get, ProfilePath, identity.Answer);
        fake.Map(HttpMethod.Put, ProfilePath, () => identity.Replace(emojiName, null));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, emojiName, "");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(fake.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Get_IdentityUnreachable_ShowsUpstreamMessageAndNoForm()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);

        // Act
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(page));
        Assert.Null(page.QuerySelector(FormSelector));
        Assert.Null(page.QuerySelector("[data-profile-account]"));
    }

    [Fact]
    public async Task Post_IdentityAnswersUnauthorized_EndsSession()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("Alice Rao", "+919876543210"));
        fake.Map(HttpMethod.Put, ProfilePath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        HttpResponseMessage response = await SubmitAsync(form, page, "Alice R. Rao", "8765432109");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login?returnUrl=%2Fprofile&reason=session-ended", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Post_IdentityFieldError_ShowsFieldMessageKeepsValues()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.ValidationProblem("phone", "The phone must be an Indian mobile number."));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "Alice R. Rao", "8765432109");

        // Assert
        Assert.Equal("Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9.", FieldError(result, "Input.Phone"));
        Assert.Equal("Alice R. Rao", Value(result, "Input.FullName"));
        Assert.Equal("8765432109", Value(result, "Input.Phone"));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamMessageKeepsValues()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("Alice Rao", "+919876543210"));
        fake.Map(HttpMethod.Put, ProfilePath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "Alice R. Rao", "8765432109");

        // Assert
        Assert.Equal("This part of RouteGo is not available right now. Please try again in a few minutes.", AlertText(result));
        Assert.Equal("Alice R. Rao", Value(result, "Input.FullName"));
        Assert.Equal("8765432109", Value(result, "Input.Phone"));
        Assert.Single(fake.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task Post_UnknownProblem_ShowsInternalError()
    {
        // Arrange
        FakeBackend fake = RefusingBackend(IdentityStubs.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake);
        IDocument page = await form.GetPageAsync(ProfilePagePath);

        // Act
        IDocument result = await SubmitAndReadAsync(form, page, "Alice R. Rao", "8765432109");

        // Assert
        Assert.Equal(
            "Something went wrong on our side. Try again — if it keeps happening, contact support with reference trace-internal_error.",
            AlertText(result));
        Assert.Equal("Alice R. Rao", Value(result, "Input.FullName"));
    }

    /// <summary>Identity's Profile answer for Alice.</summary>
    private static HttpResponseMessage ProfileAnswer(string fullName, string? phone)
    {
        var profile = new { userId = Guid.NewGuid(), email = "alice@example.com", fullName, phone, role = "customer" };
        return FakeBackend.Json(profile, HttpStatusCode.OK);
    }

    private static FakeBackend RefusingBackend(HttpResponseMessage answer)
    {
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, ProfilePath, () => ProfileAnswer("Alice Rao", "+919876543210"));
        fake.Map(HttpMethod.Put, ProfilePath, () => answer);
        return fake;
    }

    private static async Task<FormClient> SignInAsync(WebApplicationFactory<Program> factory, FakeBackend fake)
    {
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));
        return new FormClient(client);
    }

    private static async Task<HttpResponseMessage> SubmitAsync(FormClient form, IDocument page, string fullName, string phone)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.FullName"] = fullName,
            ["Input.Phone"] = phone,
        };
        return await form.SendFormAsync(page, FormSelector, fields);
    }

    private static async Task<IDocument> SubmitAndReadAsync(FormClient form, IDocument page, string fullName, string phone)
    {
        HttpResponseMessage response = await SubmitAsync(form, page, fullName, phone);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static string[] Labels(IDocument page)
    {
        return page.QuerySelectorAll(FormSelector + " label").Select(label => label.TextContent.Trim()).ToArray();
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

    /// <summary>Identity's copy of Alice's profile: answers the profile, and a save replaces it.</summary>
    private sealed class SavedProfile
    {
        private string _fullName;
        private string? _phone;

        public SavedProfile(string fullName, string? phone)
        {
            _fullName = fullName;
            _phone = phone;
        }

        public HttpResponseMessage Answer()
        {
            return ProfileAnswer(_fullName, _phone);
        }

        public HttpResponseMessage Replace(string fullName, string? phone)
        {
            _fullName = fullName;
            _phone = phone;
            return Answer();
        }
    }
}
