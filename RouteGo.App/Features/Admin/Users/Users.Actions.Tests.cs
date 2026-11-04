using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Admin.Users;

public sealed class UsersActionsTests
{
    private const string SpamEmail = "spam@example.com";
    private const string ConfirmForm = "form[data-confirm-form]";

    private static readonly string _suspendPath = $"/auth/admin/users/{UsersTestData.SpamId}/suspend";
    private static readonly string _reactivatePath = $"/auth/admin/users/{UsersTestData.SpamId}/reactivate";

    [Fact]
    public async Task Post_SuspendWithReason_SuspendsAndReloadsList()
    {
        // Arrange
        var fake = new FakeBackend();
        var accounts = new SpamAccount("active");
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, accounts.Search);
        fake.Map(HttpMethod.Post, _suspendPath, () => accounts.Change("suspended"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{UsersTestData.UsersPath}?status=active&suspend={UsersTestData.SpamId}");

        // Act
        HttpResponseMessage response = await ConfirmAsync(form, page, "Repeated fake bookings reported by operators");
        string? sentBody = fake.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(
            "Suspend spam@example.com? They will be signed out within 15 minutes and cannot sign in until an admin reactivates them.",
            page.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Suspend account", page.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/users?status=active&pageNumber=1", response.Headers.Location!.OriginalString);
        Assert.Equal("{\"reason\":\"Repeated fake bookings reported by operators\"}", sentBody);
        Assert.Equal("spam@example.com is suspended. They will be signed out within 15 minutes.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Suspended", result.QuerySelector("[data-user-status]")!.TextContent);
        Assert.Null(result.QuerySelector("[data-confirm]"));
    }

    [Fact]
    public async Task Post_ReasonTooShort_ShowsMessageWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        var accounts = new SpamAccount("active");
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, accounts.Search);
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{UsersTestData.UsersPath}?suspend={UsersTestData.SpamId}");

        // Act
        IDocument result = await ConfirmAndReadAsync(form, page, "spam");

        // Assert
        Assert.Equal("Reason must be between 10 and 500 characters.", result.QuerySelector("[data-valmsg-for='Input.Reason']")!.TextContent.Trim());
        Assert.Equal("spam", result.QuerySelector("textarea[name='Input.Reason']")!.TextContent.Trim());
        Assert.NotNull(result.QuerySelector("[data-confirm]"));
        Assert.DoesNotContain(fake.Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == _suspendPath);
    }

    [Fact]
    public async Task Get_Cancel_ReturnsToListWithoutChangingAnything()
    {
        // Arrange
        var fake = new FakeBackend();
        var accounts = new SpamAccount("active");
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, accounts.Search);
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{UsersTestData.UsersPath}?email=spam&suspend={UsersTestData.SpamId}");
        string cancelHref = page.QuerySelector("[data-cancel]")!.GetAttribute("href")!;

        // Act
        IDocument result = await form.GetPageAsync(cancelHref);

        // Assert
        Assert.Equal("/admin/users?email=spam&pageNumber=1", cancelHref);
        Assert.Null(result.QuerySelector("[data-confirm]"));
        Assert.Equal("Active", result.QuerySelector("[data-user-status]")!.TextContent);
        Assert.DoesNotContain(fake.Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith(UsersTestData.SearchPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_ReactivateWithReason_ReactivatesAndReloadsList()
    {
        // Arrange
        var fake = new FakeBackend();
        var accounts = new SpamAccount("suspended");
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, accounts.Search);
        fake.Map(HttpMethod.Post, _reactivatePath, () => accounts.Change("active"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{UsersTestData.UsersPath}?reactivate={UsersTestData.SpamId}");

        // Act
        HttpResponseMessage response = await ConfirmAsync(form, page, "Appeal reviewed and accepted");
        string? sentBody = fake.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal("Reactivate spam@example.com? They will be able to sign in again.", page.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Reactivate account", page.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal("{\"reason\":\"Appeal reviewed and accepted\"}", sentBody);
        Assert.Equal("spam@example.com is active again. They can sign in now.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal("Active", result.QuerySelector("[data-user-status]")!.TextContent);
    }

    [Theory]
    [InlineData("suspend", "USER_NOT_ACTIVE", "suspended", "This account is no longer active. It is now Suspended. The list has been updated.")]
    [InlineData("reactivate", "USER_NOT_SUSPENDED", "active", "This account is not suspended. It is now Active. The list has been updated.")]
    [InlineData("suspend", "ADMIN_CANNOT_BE_SUSPENDED", "", "Admin accounts cannot be suspended.")]
    [InlineData("suspend", "NOT_FOUND", "", "We couldn't find that account. It may have been removed.")]
    public async Task Post_AccountChangedMeanwhile_TellsWhyAndReloadsList(string action, string code, string currentStatus, string expected)
    {
        // Arrange
        var fake = new FakeBackend();
        var accounts = new SpamAccount(StartingStatus(action));
        var extensions = new Dictionary<string, object> { ["currentStatus"] = currentStatus };
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, accounts.Search);
        fake.Map(HttpMethod.Post, $"/auth/admin/users/{UsersTestData.SpamId}/{action}", () => IdentityStubs.Problem(HttpStatusCode.Conflict, code, extensions));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{UsersTestData.UsersPath}?{action}={UsersTestData.SpamId}");
        int searchesBefore = SearchCount(fake);

        // Act
        HttpResponseMessage response = await ConfirmAsync(form, page, "Repeated fake bookings reported by operators");
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal(searchesBefore + 1, SearchCount(fake));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_KeepsConfirmationAndReason()
    {
        // Arrange
        var fake = new FakeBackend();
        var accounts = new SpamAccount("active");
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, accounts.Search);
        fake.Map(HttpMethod.Post, _suspendPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{UsersTestData.UsersPath}?suspend={UsersTestData.SpamId}");

        // Act
        IDocument result = await ConfirmAndReadAsync(form, page, "Repeated fake bookings reported by operators");

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Repeated fake bookings reported by operators", result.QuerySelector("textarea[name='Input.Reason']")!.TextContent.Trim());
        Assert.Equal(
            "Suspend spam@example.com? They will be signed out within 15 minutes and cannot sign in until an admin reactivates them.",
            result.QuerySelector("[data-confirm-question]")!.TextContent);
    }

    private static string StartingStatus(string action)
    {
        if (action == "reactivate")
        {
            return "suspended";
        }

        return "active";
    }

    private static int SearchCount(FakeBackend fake)
    {
        return fake.Requests.Count(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == UsersTestData.SearchPath);
    }

    // Sends the open confirmation with its hidden account fields, as the browser would.
    private static async Task<HttpResponseMessage> ConfirmAsync(FormClient form, IDocument page, string reason)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.UserId"] = page.QuerySelector(ConfirmForm + " input[name='Input.UserId']")!.GetAttribute("value")!,
            ["Input.Email"] = page.QuerySelector(ConfirmForm + " input[name='Input.Email']")!.GetAttribute("value")!,
            ["Input.Reason"] = reason,
        };
        return await form.SendFormAsync(page, ConfirmForm, fields);
    }

    private static async Task<IDocument> ConfirmAndReadAsync(FormClient form, IDocument page, string reason)
    {
        HttpResponseMessage response = await ConfirmAsync(form, page, reason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    /// <summary>Identity's copy of spam@example.com: the search lists it, and a change updates its status.</summary>
    private sealed class SpamAccount
    {
        private string _status;

        public SpamAccount(string status)
        {
            _status = status;
        }

        public HttpResponseMessage Search()
        {
            return UsersTestData.Answer(1, 1, UsersTestData.User(UsersTestData.SpamId, SpamEmail, "customer", _status));
        }

        public HttpResponseMessage Change(string status)
        {
            _status = status;
            return FakeBackend.Json(UsersTestData.User(UsersTestData.SpamId, SpamEmail, "customer", status), HttpStatusCode.OK);
        }
    }
}
