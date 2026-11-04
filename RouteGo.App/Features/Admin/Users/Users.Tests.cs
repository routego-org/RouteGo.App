using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Admin.Users;

public sealed class UsersTests
{
    [Fact]
    public async Task Get_Admin_ListsAllAccountsNewestFirstWithFilters()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, () => UsersTestData.Answer(2, 1,
            UsersTestData.User(UsersTestData.AliceId, "alice@example.com", "customer", "active"),
            UsersTestData.User(UsersTestData.RaviId, "ravi@busco.in", "operator", "pendingApproval")));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        string[] columns = new[] { "Email", "Full name", "Mobile number", "Role", "Status", "Created at", "" };

        // Act
        IDocument page = await form.GetPageAsync(UsersTestData.UsersPath);

        // Assert
        Assert.Equal("?page=1&pageSize=20", UsersTestData.LastSearchQuery(fake));
        Assert.Equal("Users", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(new[] { "Email", "Role", "Status" }, UsersTestData.Texts(page, "[data-users-filters] label"));
        Assert.Equal("Search", page.QuerySelector("[data-users-filters] button")!.TextContent);
        Assert.Equal("/admin/users/new", page.QuerySelector("[data-create-admin]")!.GetAttribute("href"));
        Assert.Equal(columns, UsersTestData.Texts(page, "[data-users-table] th"));
        Assert.Equal(
            new[] { "alice@example.com", "Alice Rao", "+919876543210", "Customer", "Active", "2 Oct 2026, 3:30 PM IST" },
            UsersTestData.Texts(page, "[data-user-row='alice@example.com'] td").Take(6).ToArray());
        Assert.Equal("Waiting for approval", page.QuerySelector("[data-user-row='ravi@busco.in'] [data-user-status]")!.TextContent);
        Assert.Equal("Showing 1–2 of 2 accounts", page.QuerySelector("[data-users-showing]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-users-pages]"));
    }

    [Fact]
    public async Task Get_WithFilters_SendsThemAndKeepsThemInTheForm()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, () => UsersTestData.Answer(2, 1,
            UsersTestData.User(UsersTestData.AliceId, "alice@example.com", "customer", "suspended"),
            UsersTestData.User(UsersTestData.SpamId, "alicia@example.com", "customer", "suspended")));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(UsersTestData.UsersPath + "?email=alic&role=customer&status=suspended");

        // Assert
        Assert.Equal("?email=alic&role=customer&status=suspended&page=1&pageSize=20", UsersTestData.LastSearchQuery(fake));
        Assert.Equal(2, page.QuerySelectorAll("[data-user-row]").Length);
        Assert.Equal("Showing 1–2 of 2 accounts", page.QuerySelector("[data-users-showing]")!.TextContent);
        Assert.Equal("alic", page.QuerySelector("#filter-email")!.GetAttribute("value"));
        Assert.Equal("customer", page.QuerySelector("#filter-role option[selected]")!.GetAttribute("value"));
        Assert.Equal("suspended", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
    }

    [Theory]
    [InlineData("?email=zzz-nobody", "No accounts match your search.")]
    [InlineData("", "No accounts yet.")]
    public async Task Get_NothingMatches_ShowsEmptyMessage(string query, string expected)
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, () => UsersTestData.Answer(0, 1));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(UsersTestData.UsersPath + query);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-users-empty]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-users-table]"));
    }

    [Fact]
    public async Task Get_MixedAccounts_OffersActionsOnlyWhereAllowed()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, () => UsersTestData.Answer(5, 1,
            UsersTestData.User(Guid.NewGuid(), "active-customer@example.com", "customer", "active"),
            UsersTestData.User(Guid.NewGuid(), "active-operator@example.com", "operator", "active"),
            UsersTestData.User(Guid.NewGuid(), "suspended-customer@example.com", "customer", "suspended"),
            UsersTestData.User(Guid.NewGuid(), "admin@example.com", "admin", "active"),
            UsersTestData.User(Guid.NewGuid(), "waiting-operator@example.com", "operator", "pendingApproval")));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(UsersTestData.UsersPath);

        // Assert
        Assert.Equal(
            new[] { "active-customer@example.com", "active-operator@example.com" },
            UsersTestData.RowsWith(page, "[data-suspend]"));
        Assert.Equal(new[] { "suspended-customer@example.com" }, UsersTestData.RowsWith(page, "[data-reactivate]"));
    }

    [Fact]
    public async Task Get_SecondPage_SendsSameFiltersAndShowsRange()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, () => UsersTestData.Answer(45, 2, UsersTestData.Users(20)));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(UsersTestData.UsersPath + "?role=customer&pageNumber=2");

        // Assert
        Assert.Equal("?role=customer&page=2&pageSize=20", UsersTestData.LastSearchQuery(fake));
        Assert.Equal("Showing 21–40 of 45 accounts", page.QuerySelector("[data-users-showing]")!.TextContent);
        Assert.Equal(new[] { "1", "2", "3" }, UsersTestData.Texts(page, "[data-users-pages] a"));
        Assert.Equal("2", page.QuerySelector("[data-users-pages] .active a")!.TextContent);
        Assert.Equal("/admin/users?role=customer&pageNumber=3", page.QuerySelectorAll("[data-users-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_IdentityUnreachable_ShowsUpstreamMessageKeepsFilters()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(UsersTestData.UsersPath + "?email=alic");

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("alic", page.QuerySelector("#filter-email")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-users-table]"));
    }

    [Fact]
    public async Task Get_IdentityRefusesFilter_ShowsFormatMessageOnField()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, UsersTestData.SearchPath, () => IdentityStubs.ValidationProblem("email", "'Email' must be 254 characters or fewer."));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await UsersTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(UsersTestData.UsersPath + "?email=alic");

        // Assert
        Assert.Equal("Enter a valid email.", page.QuerySelector("[data-valmsg-for='Query.Email']")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-users-table]"));
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
        HttpResponseMessage response = await client.GetAsync(UsersTestData.UsersPath);
        string html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("You don&#x27;t have permission to open this page.", html);
        Assert.Contains("Go to Home", html);
        Assert.DoesNotContain("data-users-table", html);
        Assert.Equal(callsBefore, fake.Requests.Count);
        Assert.Contains("data-signed-in-as", html);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithReturnAddress()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(UsersTestData.UsersPath);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login?returnUrl=%2Fadmin%2Fusers&reason=sign-in-required", response.Headers.Location!.PathAndQuery);
    }
}

/// <summary>Identity's account-list answers and the steps every Users test shares.</summary>
internal static class UsersTestData
{
    public const string UsersPath = "/admin/users";
    public const string SearchPath = "/auth/admin/users";

    public static readonly Guid AliceId = new Guid("11111111-1111-1111-1111-111111111111");
    public static readonly Guid RaviId = new Guid("22222222-2222-2222-2222-222222222222");
    public static readonly Guid SpamId = new Guid("33333333-3333-3333-3333-333333333333");

    public static async Task<FormClient> SignInAsync(WebApplicationFactory<Program> factory, FakeBackend fake, string role)
    {
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("priya@example.com", role));
        return new FormClient(client);
    }

    /// <summary>One account as Identity's search lists it.</summary>
    public static object User(Guid userId, string email, string role, string status)
    {
        return new
        {
            userId,
            email,
            fullName = "Alice Rao",
            phone = "+919876543210",
            role,
            status,
            createdAt = "2026-10-02T10:00:00Z",
        };
    }

    /// <summary>A given number of active customers, for a full page.</summary>
    public static object[] Users(int count)
    {
        var users = new List<object>();
        for (int index = 0; index < count; index++)
        {
            users.Add(User(Guid.NewGuid(), $"user{index}@example.com", "customer", "active"));
        }

        return users.ToArray();
    }

    public static HttpResponseMessage Answer(int totalCount, int page, params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount, page, pageSize = 20 }, HttpStatusCode.OK);
    }

    public static string LastSearchQuery(FakeBackend fake)
    {
        HttpRequestMessage search = fake.Requests.Last(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == SearchPath);
        return search.RequestUri!.Query;
    }

    public static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    /// <summary>The emails of the rows that offer the given action.</summary>
    public static string[] RowsWith(IDocument page, string actionSelector)
    {
        IEnumerable<IElement> rows = page.QuerySelectorAll("[data-user-row]").Where(row => row.QuerySelector(actionSelector) != null);
        return rows.Select(row => row.GetAttribute("data-user-row")!).ToArray();
    }
}
