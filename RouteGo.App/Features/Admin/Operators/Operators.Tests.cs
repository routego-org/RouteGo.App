using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Admin.Operators;

public sealed class OperatorsTests
{
    [Fact]
    public async Task Get_Admin_ListsWaitingApplicationsWithTheirDetails()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, () => OperatorsTestData.Answer(1, 1, OperatorsTestData.Application("pendingApproval")));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        string[] labels = new[] { "Company name", "GSTIN", "Contact email", "Email", "Full name", "Submitted at", "Status" };

        // Act
        IDocument page = await form.GetPageAsync(OperatorsTestData.OperatorsPath);

        // Assert
        Assert.Equal("?page=1&pageSize=20", OperatorsTestData.LastListQuery(fake));
        Assert.Equal("Operator applications", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("pendingApproval", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Equal("Waiting for approval", page.QuerySelector("#filter-status option[selected]")!.TextContent);
        Assert.Equal("Search", page.QuerySelector("[data-operators-filters] button")!.TextContent);
        Assert.Equal(labels, OperatorsTestData.Texts(page, "[data-application-fields] dt"));
        Assert.Equal(
            new[] { "BusCo Travels", "27AAPFU0939F1ZV", "ops@busco.in", "ravi@busco.in", "Ravi Kumar", "2 Oct 2026, 3:30 PM IST", "Waiting for approval" },
            OperatorsTestData.Texts(page, "[data-application-fields] dd"));
        Assert.NotNull(page.QuerySelector("[data-approve]"));
        Assert.NotNull(page.QuerySelector("[data-reject]"));
        Assert.Equal("Showing 1–1 of 1 applications", page.QuerySelector("[data-operators-showing]")!.TextContent);
    }

    [Fact]
    public async Task Get_EarlierRejection_ShowsReasonWithBothDates()
    {
        // Arrange
        var fake = new FakeBackend();
        object rejection = new { applicationId = Guid.NewGuid(), submittedAt = "2026-10-01T04:30:00Z", rejectedAt = "2026-10-01T06:00:00Z", reason = "GSTIN does not match company name" };
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, () => OperatorsTestData.Answer(1, 1, OperatorsTestData.Application("pendingApproval", rejection)));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(OperatorsTestData.OperatorsPath);

        // Assert
        Assert.Equal("Earlier rejection: GSTIN does not match company name", page.QuerySelector("[data-earlier-reason]")!.TextContent);
        Assert.Equal("Submitted on 1 Oct 2026, 10:00 AM IST", page.QuerySelector("[data-earlier-submitted]")!.TextContent);
        Assert.Equal("Rejected on 1 Oct 2026, 11:30 AM IST", page.QuerySelector("[data-earlier-rejected]")!.TextContent);
    }

    [Fact]
    public async Task Get_RejectedFilter_ShowsReviewAndReasonWithoutDecisions()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, () => OperatorsTestData.Answer(1, 1, OperatorsTestData.Application("rejected")));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(OperatorsTestData.OperatorsPath + "?status=rejected");

        // Assert
        Assert.Equal("?status=rejected&page=1&pageSize=20", OperatorsTestData.LastListQuery(fake));
        Assert.Equal("rejected", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Contains("Reviewed at", OperatorsTestData.Texts(page, "[data-application-fields] dt"));
        Assert.Contains("Rejection reason", OperatorsTestData.Texts(page, "[data-application-fields] dt"));
        Assert.Contains("Company documents are unreadable", OperatorsTestData.Texts(page, "[data-application-fields] dd"));
        Assert.Null(page.QuerySelector("[data-approve]"));
        Assert.Null(page.QuerySelector("[data-reject]"));
    }

    [Theory]
    [InlineData("", "No applications are waiting for review.")]
    [InlineData("?status=approved", "No applications match the current filters.")]
    public async Task Get_NothingMatches_ShowsEmptyMessage(string query, string expected)
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, () => OperatorsTestData.Answer(0, 1));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(OperatorsTestData.OperatorsPath + query);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-operators-empty]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-application]"));
    }

    [Fact]
    public async Task Get_SecondPage_SendsSameFilterAndShowsRange()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, () => OperatorsTestData.Answer(45, 2, OperatorsTestData.Applications(20)));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(OperatorsTestData.OperatorsPath + "?status=approved&pageNumber=2");

        // Assert
        Assert.Equal("?status=approved&page=2&pageSize=20", OperatorsTestData.LastListQuery(fake));
        Assert.Equal("Showing 21–40 of 45 applications", page.QuerySelector("[data-operators-showing]")!.TextContent);
        Assert.Equal(new[] { "1", "2", "3" }, OperatorsTestData.Texts(page, "[data-operators-pages] a"));
        Assert.Equal("/admin/operators?status=approved&pageNumber=3", page.QuerySelectorAll("[data-operators-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_IdentityUnreachable_ShowsUpstreamMessageKeepsFilter()
    {
        // Arrange
        var fake = new FakeBackend();
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(OperatorsTestData.OperatorsPath + "?status=rejected");

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("rejected", page.QuerySelector("#filter-status option[selected]")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-application]"));
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
        HttpResponseMessage response = await client.GetAsync(OperatorsTestData.OperatorsPath);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(callsBefore, fake.Requests.Count);
    }
}

/// <summary>Identity's application-list answers and the steps every Operators test shares.</summary>
internal static class OperatorsTestData
{
    public const string OperatorsPath = "/admin/operators";
    public const string ListPath = "/operators/admin/applications";

    public static readonly Guid BusCoId = new Guid("55555555-5555-5555-5555-555555555555");

    public static async Task<FormClient> SignInAsync(WebApplicationFactory<Program> factory, FakeBackend fake, string role)
    {
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("priya@example.com", role));
        return new FormClient(client);
    }

    /// <summary>BusCo Travels' latest application in a status, with any earlier rejections.</summary>
    public static object Application(string status, params object[] previousRejections)
    {
        return Application(BusCoId, "BusCo Travels", status, previousRejections);
    }

    public static object Application(Guid operatorId, string companyName, string status, params object[] previousRejections)
    {
        string? reviewedAt = null;
        if (status == "approved" || status == "rejected")
        {
            reviewedAt = "2026-10-03T10:00:00Z";
        }

        string? rejectReason = null;
        if (status == "rejected")
        {
            rejectReason = "Company documents are unreadable";
        }

        return new
        {
            applicationId = Guid.NewGuid(),
            operatorId,
            email = "ravi@busco.in",
            fullName = "Ravi Kumar",
            companyName,
            contactEmail = "ops@busco.in",
            gstin = "27AAPFU0939F1ZV",
            status,
            submittedAt = "2026-10-02T10:00:00Z",
            reviewedAt,
            rejectReason,
            previousRejections,
        };
    }

    /// <summary>A given number of approved applications, for a full page.</summary>
    public static object[] Applications(int count)
    {
        var applications = new List<object>();
        for (int index = 0; index < count; index++)
        {
            applications.Add(Application(Guid.NewGuid(), $"Company {index}", "approved"));
        }

        return applications.ToArray();
    }

    public static HttpResponseMessage Answer(int totalCount, int page, params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount, page, pageSize = 20 }, HttpStatusCode.OK);
    }

    public static string LastListQuery(FakeBackend fake)
    {
        HttpRequestMessage list = fake.Requests.Last(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == ListPath);
        return list.RequestUri!.Query;
    }

    public static int ListCount(FakeBackend fake)
    {
        return fake.Requests.Count(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == ListPath);
    }

    public static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
