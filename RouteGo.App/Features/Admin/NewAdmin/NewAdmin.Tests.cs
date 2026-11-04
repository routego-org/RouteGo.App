using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Admin.NewAdmin;

public sealed class NewAdminTests
{
    private const string NewAdminPath = "/admin/users/new";
    private const string AdminUsersPath = "/auth/admin/users";
    private const string FormSelector = "form[data-new-admin-form]";
    private const string NewEmail = "support2@routego.in";

    [Fact]
    public async Task Get_Admin_ShowsFormHintAndBackLink()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(NewAdminPath);

        // Assert
        Assert.Equal("Create admin", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(new[] { "Email", "Password", "Full name", "Mobile number (optional)" }, Texts(page, FormSelector + " label"));
        Assert.Equal(
            "RouteGo does not email this password. Give it to the new admin yourself. They cannot change it later.",
            page.QuerySelector("[data-password-hint]")!.TextContent);
        Assert.Equal("Create admin", page.QuerySelector(FormSelector + " button")!.TextContent);
        Assert.Equal("Back to users", page.QuerySelector("[data-back-to-users]")!.TextContent);
        Assert.Equal("/admin/users", page.QuerySelector("[data-back-to-users]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Post_ValidAdmin_CreatesAndShowsAccountInUsersList()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, AdminUsersPath, () => FakeBackend.Json(Account("active"), HttpStatusCode.Created));
        fake.Map(HttpMethod.Get, AdminUsersPath, () => FakeBackend.Json(new { items = new[] { Account("active") }, totalCount = 1, page = 1, pageSize = 20 }, HttpStatusCode.OK));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync(NewAdminPath);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, FormSelector, Fields(NewEmail, "welcome2026a", "Support Two"));
        JsonElement sent = JsonDocument.Parse(fake.LastRequestBody!).RootElement;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/users?email=support2@routego.in", Uri.UnescapeDataString(response.Headers.Location!.OriginalString));
        Assert.Equal(NewEmail, sent.GetProperty("email").GetString());
        Assert.Equal("welcome2026a", sent.GetProperty("password").GetString());
        Assert.Equal("Support Two", sent.GetProperty("fullName").GetString());
        Assert.Equal("Admin account created.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal(
            new[] { NewEmail, "Support Two", "", "Admin", "Active" },
            Texts(result, $"[data-user-row='{NewEmail}'] td").Take(5).ToArray());
    }

    [Fact]
    public async Task Post_EmailAlreadyRegistered_ShowsMessageOnEmailAndKeepsValuesExceptPassword()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, AdminUsersPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "EMAIL_ALREADY_REGISTERED"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync(NewAdminPath);

        // Act
        IDocument result = await SendAndReadAsync(form, page, Fields(NewEmail, "welcome2026a", "Support Two"));

        // Assert
        Assert.Equal(
            "An account already uses this email. Each email can have only one account and one role.",
            FieldMessage(result, "Input.Email"));
        Assert.Equal(NewEmail, result.QuerySelector("input[name='Input.Email']")!.GetAttribute("value"));
        Assert.Equal("Support Two", result.QuerySelector("input[name='Input.FullName']")!.GetAttribute("value"));
        Assert.Equal("", result.QuerySelector("input[name='Input.Password']")!.GetAttribute("value") ?? "");
    }

    [Theory]
    [InlineData("short1", "Support Two", "Input.Password", "Password must be at least 10 characters and have at least one letter and one number.")]
    [InlineData("welcome2026a", "S", "Input.FullName", "Full name must be at least 2 characters.")]
    public async Task Post_FieldBreaksRule_ShowsMessageWithoutCallingIdentity(string password, string fullName, string field, string expected)
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync(NewAdminPath);

        // Act
        IDocument result = await SendAndReadAsync(form, page, Fields(NewEmail, password, fullName));

        // Assert
        Assert.Equal(expected, FieldMessage(result, field));
        Assert.DoesNotContain(fake.Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == AdminUsersPath);
    }

    [Fact]
    public async Task Post_IdentityRefusesField_ShowsCatalogMessageOnField()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, AdminUsersPath, () => IdentityStubs.ValidationProblem("phone", "'Phone' is not in the correct format."));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync(NewAdminPath);

        // Act
        IDocument result = await SendAndReadAsync(form, page, Fields(NewEmail, "welcome2026a", "Support Two"));

        // Assert
        Assert.Equal("Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9.", FieldMessage(result, "Input.Phone"));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_ShowsUpstreamMessageAndKeepsValues()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Post, AdminUsersPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync(NewAdminPath);

        // Act
        IDocument result = await SendAndReadAsync(form, page, Fields(NewEmail, "welcome2026a", "Support Two"));

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(NewEmail, result.QuerySelector("input[name='Input.Email']")!.GetAttribute("value"));
        Assert.Equal("", result.QuerySelector("input[name='Input.Password']")!.GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task Get_Customer_ShowsNoPermissionWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));
        int callsBefore = fake.Requests.Count;

        // Act
        HttpResponseMessage response = await client.GetAsync(NewAdminPath);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(callsBefore, fake.Requests.Count);
    }

    private static async Task<FormClient> SignInAsync(WebApplicationFactory<Program> factory, FakeBackend fake, string role)
    {
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("priya@example.com", role));
        return new FormClient(client);
    }

    private static async Task<IDocument> SendAndReadAsync(FormClient form, IDocument page, Dictionary<string, string> fields)
    {
        HttpResponseMessage response = await form.SendFormAsync(page, FormSelector, fields);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    private static Dictionary<string, string> Fields(string email, string password, string fullName)
    {
        return new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.FullName"] = fullName,
            ["Input.Phone"] = "",
        };
    }

    /// <summary>The new admin as Identity returns it from creation and lists it afterwards.</summary>
    private static object Account(string status)
    {
        return new
        {
            userId = new Guid("44444444-4444-4444-4444-444444444444"),
            email = NewEmail,
            fullName = "Support Two",
            phone = (string?)null,
            role = "admin",
            status,
            createdAt = "2026-10-09T10:00:00Z",
        };
    }

    private static string FieldMessage(IDocument page, string field)
    {
        return page.QuerySelector($"[data-valmsg-for='{field}']")!.TextContent.Trim();
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
